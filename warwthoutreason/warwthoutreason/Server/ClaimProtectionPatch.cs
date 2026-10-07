using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace warwthtreason.Server
{
    // Postfix on ShouldReceiveDamage — method returns bool "should the player receive damage".
    // We override the result from true to false if the damage should be blocked.
    [HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.ShouldReceiveDamage))]
    public static class ClaimProtectionPatch
    {
        static void Postfix(EntityPlayer __instance, DamageSource damageSource, ref bool __result)
        {
            // Game already decided not to deal damage — don't interfere.
            if (!__result || __instance.World?.Side != EnumAppSide.Server) return;
            if (damageSource == null) return;

            var system = CombatLogSystemAccessor.Instance;
            if (system == null || !system.Config.EnableClaimProtection) return;

            // Determine the type of damage. Responding only to PvP and PvE.
            bool isPlayerDamage = damageSource.Source == EnumDamageSource.Player;
            bool isPveDamage = damageSource.Source == EnumDamageSource.Entity;
            if (!isPlayerDamage && !isPveDamage) return;

            // Attack side: For PvP we get the attacker.
            EntityPlayer? attacker = null;
            if (isPlayerDamage)
            {
                attacker = damageSource.GetCauseEntity() as EntityPlayer;
                if (attacker == null) return;
                if (attacker.EntityId == __instance.EntityId) return;
            }

            // --- Check positions of both sides ---
            var victimClaims = system.GetClaimsAt(__instance.Pos.AsBlockPos);
            LandClaim? victimClaim = victimClaims?.Length > 0 ? victimClaims[0] : null;

            LandClaim? attackerClaim = null;
            if (attacker != null)
            {
                var attackerClaims = system.GetClaimsAt(attacker.Pos.AsBlockPos);
                attackerClaim = attackerClaims?.Length > 0 ? attackerClaims[0] : null;
            }

            // If nobody is in a claim — damage is permitted.
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

                // Per-claim flags: if in any of claims PvP is allowed — damage is permitted.
                if (victimClaim != null && system.GetClaimFlags(victimClaim)?.AllowPvP == true) return;
                if (attackerClaim != null && system.GetClaimFlags(attackerClaim)?.AllowPvP == true) return;

                // Global setting.
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