// Compatibility with DisablePvPInsideClaims.
//
// Their Postfix blocks all PvP damage inside claims, breaking the chase mechanic.
// This is resolved by adding a Prefix: in combat state, it returns false,
// forcing Harmony to skip their Postfix.
//
// If DisablePvPInsideClaims is missing, TargetMethod() returns null (the patch is skipped).

using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace warwthtreason.Server
{
    
    [HarmonyPatch]
    public static class DisablePvPInsideClaimsCombatPatch
    {
        // Resolve DisablePvPInsideClaims.DisablePvPInsideClaimsModSystem
        //   .ShouldReceiveDamagePostfix from loaded assemblies.
        static MethodBase? TargetMethod()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType("DisablePvPInsideClaims.DisablePvPInsideClaimsModSystem");
                if (type != null)
                {
                    return AccessTools.Method(type, "ShouldReceiveDamagePostfix");
                }
            }
            return null;
        }

        // __0 = EntityPlayer (victim), matches their "__instance".
        // __1 = DamageSource, matches their "damageSource".
        // Return false → skip their Postfix body.
        // Return true  → let them run and decide normally.
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

            bool attackerInCombat = system.IsInCombat(attacker);
            bool victimInCombat = system.IsInCombat(victim);

            // 1. Mutual chase — both already fighting. Skip SafeZone's check.
            if (attackerInCombat && victimInCombat)
                return false;

            // 2. Peaceful attacker in a claim strikes a combat target.
            if (!attackerInCombat && victimInCombat)
            {
                var attackerClaims = system.GetClaimsAt(attackerEntity.Pos.AsBlockPos);
                if (attackerClaims != null && attackerClaims.Length > 0)
                {
                    // 2a. Owner defense.
                    if (system.Config.AllowOwnerDefenseInClaims)
                    {
                        bool attackerIsOwner = system.Sapi.World.Claims.TryAccess(
                            attacker,
                            attackerEntity.Pos.AsBlockPos,
                            EnumBlockAccessFlags.BuildOrBreak);

                        if (attackerIsOwner) return false;
                    }

                    // 2b. Open defense.
                    if (system.Config.AllowOpenDefenseInClaims)
                        return false;
                }
            }

            // Let mod check run as usual.
            return true;
        }
    }
}