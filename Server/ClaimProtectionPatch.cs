using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace warwthtreason.Server
{
    // Postfix on EntityPlayer.ShouldReceiveDamage. Sets __result to false
    // when damage should be blocked by claim protection.
    [HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.ShouldReceiveDamage))]
    public static class ClaimProtectionPatch
    {
        static void Postfix(EntityPlayer __instance, DamageSource damageSource, ref bool __result)
        {
            if (!__result || __instance.World?.Side != EnumAppSide.Server) return;

            var system = CombatLogSystemAccessor.Instance;
            if (system == null || !system.Config.EnableClaimProtection) return;

            var ctx = DamageContextResolver.Resolve(__instance, damageSource);
            if (ctx == null) return;

            // If neither side is inside a claim, our rules don't apply.
            if (ctx.VictimClaim == null && ctx.AttackerClaim == null) return;

            // --- PvP ---
            if (ctx.IsPvP && ctx.Attacker != null)
            {
                // 1. Mutual chase — both already fighting.
                if (ctx.AttackerInCombat && ctx.VictimInCombat)
                    return;

                // 2. Peaceful attacker in a claim strikes a combat target.
                if (!ctx.AttackerInCombat && ctx.VictimInCombat && ctx.AttackerClaim != null)
                {
                    if (system.Config.AllowOwnerDefenseInClaims)
                    {
                        bool attackerIsOwner = system.Sapi.World.Claims.TryAccess(
                            ctx.Attacker,
                            ctx.AttackerEntity!.Pos.AsBlockPos,
                            EnumBlockAccessFlags.BuildOrBreak);

                        if (attackerIsOwner) return;
                    }

                    if (system.Config.AllowOpenDefenseInClaims)
                        return;
                }

                // 3. Per-claim flags.
                if (ctx.VictimClaim != null && system.GetClaimFlags(ctx.VictimClaim)?.AllowPvP == true) return;
                if (ctx.AttackerClaim != null && system.GetClaimFlags(ctx.AttackerClaim)?.AllowPvP == true) return;

                // 4. Global config fallback.
                if (!system.Config.PreventPvPInClaims) return;

                __result = false;
                return;
            }

            // --- PvE ---
            if (ctx.IsPvE)
            {
                if (ctx.VictimClaim != null && system.GetClaimFlags(ctx.VictimClaim)?.AllowPvE == true) return;
                if (!system.Config.PreventPvEInClaims) return;

                __result = false;
                return;
            }
        }
    }
}