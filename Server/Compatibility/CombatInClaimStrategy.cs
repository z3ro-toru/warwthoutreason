using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace warwthtreason.Server.Compatibility
{
    // Shared policy used by the SafeZone and DisablePvPInsideClaims override
    // patches. Both third-party mods block PvP damage inside claims with no
    // exception for combat. When ShouldSkipOuterPatch returns true, we ask
    // Harmony to skip their Prefix/Postfix body, so damage passes through.
    public static class CombatInClaimStrategy
    {
        public static bool ShouldSkipOuterPatch(Entity victimEntity, DamageSource damageSource)
        {
            var system = CombatLogSystemAccessor.Instance;
            if (system == null) return false;

            var ctx = DamageContextResolver.Resolve(victimEntity, damageSource);
            if (ctx == null || !ctx.IsPvP || ctx.Attacker == null) return false;

            // 1. Mutual chase — both already fighting. Skip the outer patch.
            if (ctx.AttackerInCombat && ctx.VictimInCombat)
                return true;

            // 2. Peaceful attacker inside a claim strikes a combat target.
            if (!ctx.AttackerInCombat && ctx.VictimInCombat && ctx.AttackerClaim != null)
            {
                // 2a. Owner defense — attacker has build rights in their claim.
                if (system.Config.AllowOwnerDefenseInClaims)
                {
                    bool attackerIsOwner = system.Sapi.World.Claims.TryAccess(
                        ctx.Attacker,
                        ctx.AttackerEntity!.Pos.AsBlockPos,
                        EnumBlockAccessFlags.BuildOrBreak);

                    if (attackerIsOwner) return true;
                }

                // 2b. Open defense — any peaceful player in any claim.
                if (system.Config.AllowOpenDefenseInClaims)
                    return true;
            }

            return false;
        }
    }
}