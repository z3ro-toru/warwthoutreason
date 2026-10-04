using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using warwthtreason.Server; // Доступ к CombatLogSystemAccessor

namespace warwthtreason.Server
{
    [HarmonyPatch]
    public static class SafeZoneCombatOverridePatch
    {
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

            if (system.IsInCombat(attacker) || system.IsInCombat(victim))
            {
                return false;
            }

            return true;
        }
    }
}