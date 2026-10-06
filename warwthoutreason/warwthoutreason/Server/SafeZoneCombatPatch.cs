using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace warwthtreason.Server
{
    // Патч на метод Prefix патча SafeZone. Возвращая false из нашего Prefix,
    // мы не даём SafeZone'вскому Prefix выполниться — и SafeZone не блокирует урон.
    [HarmonyPatch]
    public static class SafeZoneCombatOverridePatch
    {
        // TargetMethod ищет SafeZone.Patch_Entity_ReceiveDamage.Prefix в загруженных сборках.
        // Если SafeZone не установлен, возвращаем null — Harmony пропустит патч.
        static MethodBase? TargetMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var patchType = asm.GetType("SafeZone.Patch_Entity_ReceiveDamage");
                if (patchType != null)
                {
                    return AccessTools.Method(patchType, "Prefix");
                }
            }
            return null;
        }

        // __0 — первый аргумент целевого метода (Entity, получатель урона).
        // __1 — второй аргумент (DamageSource).
        static bool Prefix(Entity __0, DamageSource __1)
        {
            var system = CombatLogSystemAccessor.Instance;
            if (system == null) return true;

            var victimEntity = __0 as EntityPlayer;
            if (victimEntity?.Player is not IServerPlayer victim) return true;

            var damageSource = __1;
            if (damageSource.Source != EnumDamageSource.Player) return true;

            var attackerEntity = damageSource.GetCauseEntity() as EntityPlayer;
            if (attackerEntity?.Player is not IServerPlayer attacker) return true;

            if (attacker == victim) return true;

            // Если кто-то в бою — пропускаем проверку SafeZone (возвращаем false).
            if (system.IsInCombat(attacker) || system.IsInCombat(victim))
            {
                return false;
            }

            return true;
        }
    }
}