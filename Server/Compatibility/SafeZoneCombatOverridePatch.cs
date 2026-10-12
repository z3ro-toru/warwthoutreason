using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using warwthtreason.Server.Diagnostics;

namespace warwthtreason.Server.Compatibility
{
    [HarmonyPatch]
    public static class SafeZoneCombatOverridePatch
    {
        private static MethodBase? targetMethod;

        internal static ICoreServerAPI? Sapi;

        static bool Prepare()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var patchType = asm.GetType("SafeZone.Patch_Entity_ReceiveDamage");
                if (patchType != null)
                {
                    targetMethod = AccessTools.Method(patchType, "Prefix");
                    Sapi?.Logger.Notification($"[WarWithoutReason] SafeZone override: found patch type, Prefix={(targetMethod != null)}");
                    return targetMethod != null;
                }
            }

            Sapi?.Logger.Notification("[WarWithoutReason] SafeZone override: patch type not found at PatchAll time.");
            return false;
        }

        static MethodBase TargetMethod() => targetMethod!;

        static bool Prefix(Entity __0, DamageSource __1, ref bool __result)
        {
            var system = CombatLogSystemAccessor.Instance;
            if (system == null) return true;

            bool skip = CombatInClaimStrategy.ShouldSkipOuterPatch(__0, __1);

            WWRDebug.Log(system.Sapi, $"SafeZone prefix → returning {!skip} (skip={skip}).");

            if (skip)
            {
                // Skip SafeZone's Prefix body, but tell Harmony that it
                // "returned true" so Entity.ReceiveDamage still runs.
                __result = true;
                return false;
            }

            return true;
        }
    }
}