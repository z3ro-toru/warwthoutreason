// A patch for the Prefix method of the SafeZone patch. By returning false from our Prefix,
// we do not allow SafeZone's Prefix to be executed — and SafeZone does not block damage.
// targetMethod is looking for SafeZone.Patch_Entity_ReceiveDamage.Prefix in uploaded builds.
// If SafeZone is not installed, we return null — Harmony will skip the patch.
using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace warwthtreason.Server
{
    
    [HarmonyPatch]
    public static class SafeZoneCombatOverridePatch
    {
#pragma warning disable CA1859
        
        static MethodBase? TargetMethod()
#pragma warning restore CA1859
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

        // __0 — 1st arg of target method (Entity, damage recipient).
        // __1 — 2nd arg (DamageSource).
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

            // If either the attacker or the victim is in combat, we do not block damage.
            if (system.IsInCombat(attacker) || system.IsInCombat(victim))
            {
                return false;
            }

            return true;
        }
    }
}