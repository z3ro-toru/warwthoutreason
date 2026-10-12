using HarmonyLib;
using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace warwthtreason.Server.Compatibility
{
    // Override patch for DisablePvPInsideClaims' Postfix.
    // Same pattern as SafeZoneCombatOverridePatch — we intercept before
    // their logic runs and skip their body when our combat exception applies.
    [HarmonyPatch]
    public static class DisablePvPInsideClaimsCombatPatch
    {
        private static MethodBase? targetMethod;

        // Reference to the mod system, so Prepare can log via sapi.
        internal static ICoreServerAPI? Sapi;

        static bool Prepare()
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType("DisablePvPInsideClaims.DisablePvPInsideClaimsModSystem");
                    if (type != null)
                    {
                        targetMethod =  AccessTools.Method(type, "ShouldReceiveDamagePostfix");
                        Sapi?.Logger.Notification($"[WarWithoutReason] DisablePvPInsideClaims override: found patch type, Prefix={(targetMethod != null)}");
                        return targetMethod != null;
                    }
                }
                Sapi?.Logger.Notification("[WarWithoutReason] DisablePvPInsideClaims override: patch type not found at PatchAll time.");
                return false;
            }

        static MethodBase TargetMethod() => targetMethod!;

        // Intercepts DisablePvPInsideClaims' Postfix. Args mirror that Postfix:
        //   __0 = EntityPlayer (instance)
        //   __1 = DamageSource
        // Returning false skips their Postfix body, so their claim check doesn't run.
        static bool Prefix(Entity __0, DamageSource __1)
        {
            var system = CombatLogSystemAccessor.Instance;
            if (system == null) return true;

            var ctx = DamageContextResolver.Resolve(__0, __1);
            if (ctx == null || !ctx.IsPvP || ctx.Attacker == null)
                return true;

            system.Sapi.Logger.Notification(
                $"[WWR DEBUG] ShouldSkip: attackerInCombat={ctx.AttackerInCombat}, " +
                $"victimInCombat={ctx.VictimInCombat}, " +
                $"attackerClaim={ctx.AttackerClaim != null}, victimClaim={ctx.VictimClaim != null}, " +
                $"ownerDefense={system.Config.AllowOwnerDefenseInClaims}, " +
                $"openDefense={system.Config.AllowOpenDefenseInClaims}");

            // false — пропустить SafeZone'вский Prefix.
            // true  — дать SafeZone выполниться.
            return !CombatInClaimStrategy.ShouldSkipOuterPatch(__0, __1);
        }
    }
}