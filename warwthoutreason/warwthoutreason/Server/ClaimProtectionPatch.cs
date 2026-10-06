using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace warwthtreason.Server
{
    // Postfix на ShouldReceiveDamage — метод возвращает bool "должен ли игрок получить урон".
    // Мы переигрываем результат с true на false, если урон должен быть заблокирован.
    [HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.ShouldReceiveDamage))]
    public static class ClaimProtectionPatch
    {
        static void Postfix(EntityPlayer __instance, DamageSource damageSource, ref bool __result)
        {
            // Игра уже решила не наносить урон — не вмешиваемся.
            if (!__result || __instance.World?.Side != EnumAppSide.Server) return;
            if (damageSource == null) return;

            var system = CombatLogSystemAccessor.Instance;
            if (system == null || !system.Config.EnableClaimProtection) return;

            // Определяем тип урона. Реагируем только на PvP и PvE.
            bool isPlayerDamage = damageSource.Source == EnumDamageSource.Player;
            bool isPveDamage = damageSource.Source == EnumDamageSource.Entity;
            if (!isPlayerDamage && !isPveDamage) return;

            // Для PvP получаем атакующего.
            EntityPlayer? attacker = null;
            if (isPlayerDamage)
            {
                attacker = damageSource.GetCauseEntity() as EntityPlayer;
                if (attacker == null) return;
                if (attacker.EntityId == __instance.EntityId) return;
            }

            // --- Проверяем позиции обеих сторон ---
            var victimClaims = system.GetClaimsAt(__instance.Pos.AsBlockPos);
            LandClaim? victimClaim = victimClaims?.Length > 0 ? victimClaims[0] : null;

            LandClaim? attackerClaim = null;
            if (attacker != null)
            {
                var attackerClaims = system.GetClaimsAt(attacker.Pos.AsBlockPos);
                attackerClaim = attackerClaims?.Length > 0 ? attackerClaims[0] : null;
            }

            // Если никто не в привате — урон проходит по стандартным правилам.
            if (victimClaim == null && attackerClaim == null) return;

            // --- PvP ---
            if (isPlayerDamage && attacker != null)
            {
                // Исключение для боя: если кто-то из участников "в бою", урон разрешён.
                var attackerPlayer = attacker.Player as IServerPlayer;
                var victimPlayer = __instance.Player as IServerPlayer;

                if (attackerPlayer != null && victimPlayer != null)
                {
                    if (system.IsInCombat(attackerPlayer) || system.IsInCombat(victimPlayer))
                        return;
                }

                // Per-claim флаги: если в любом из приватов PvP разрешён — урон проходит.
                if (victimClaim != null && system.GetClaimFlags(victimClaim)?.AllowPvP == true) return;
                if (attackerClaim != null && system.GetClaimFlags(attackerClaim)?.AllowPvP == true) return;

                // Глобальная настройка.
                if (!system.Config.PreventPvPInClaims) return;
                __result = false;
                return;
            }

            // --- PvE ---
            if (isPveDamage)
            {
                if (victimClaim != null && system.GetClaimFlags(victimClaim)?.AllowPvE == true) return;
                if (!system.Config.PreventPvEInClaims) return;
                __result = false;
                return;
            }
        }
    }
}