using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace warwthtreason.Server.Compatibility
{
    // Override patch for DisablePvPInsideClaims' Postfix.
    // Same pattern as SafeZoneCombatOverridePatch — we intercept before
    // their logic runs and skip their body when our combat exception applies.
    [HarmonyPatch]
    public static class DisablePvPInsideClaimsCombatPatch
    {
        private static MethodBase? targetMethod;

            static bool Prepare()
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType("DisablePvPInsideClaims.DisablePvPInsideClaimsModSystem");
                    if (type != null)
                    {
                        targetMethod =  AccessTools.Method(type, "ShouldReceiveDamagePostfix");
                        return targetMethod != null;
                    }
                }
                return false;
            }

        // Their Postfix signature: (EntityPlayer instance, DamageSource source, ref bool result)
        static bool Prefix(EntityPlayer __0, DamageSource __1)
        {
            return !CombatInClaimStrategy.ShouldSkipOuterPatch(__0, __1);
        }
    }
}