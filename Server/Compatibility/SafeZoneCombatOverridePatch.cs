using HarmonyLib;
using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using warwthtreason.Server.Compatibility;

namespace warwthtreason.Server
{
    [HarmonyPatch]
    public static class SafeZoneCombatOverridePatch
    {
        // Cached MethodBase resolved once in Prepare().
        private static MethodBase? targetMethod;

        // Prepare runs before Harmony attempts to resolve the target.
        // Returning false tells Harmony to skip this patch entirely.
        // This is the correct way to conditionally apply a patch when the
        // target mod may not be installed.
        static bool Prepare()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var patchType = asm.GetType("SafeZone.Patch_Entity_ReceiveDamage");
                if (patchType != null)
                {
                    targetMethod = AccessTools.Method(patchType, "Prefix");
                    return targetMethod != null;
                }
            }
            return false;
        }

        // TargetMethod only runs if Prepare() returned true.
        static MethodBase TargetMethod() => targetMethod!;

        static bool Prefix(Entity __0, DamageSource __1)
        {
            return !CombatInClaimStrategy.ShouldSkipOuterPatch(__0, __1);
        }
    }
}