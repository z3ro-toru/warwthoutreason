using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace warwthtreason.Server
{
    // Snapshot of everything the claim-protection patches need to make a decision:
    // who was hit, who hit them, which claims they stand in, and whether each
    // side is currently in combat. Resolved once per damage event, then read
    // by all three patch handlers instead of each one doing its own lookups.
    public class DamageContext
    {
        public IServerPlayer Victim = null!;
        public EntityPlayer VictimEntity = null!;
        public IServerPlayer? Attacker;         // null for PvE (mob damage)
        public EntityPlayer? AttackerEntity;
        public LandClaim? VictimClaim;
        public LandClaim? AttackerClaim;
        public bool IsPvP;
        public bool IsPvE;
        public bool AttackerInCombat;
        public bool VictimInCombat;
    }

    public static class DamageContextResolver
    {
        // Builds a DamageContext, or returns null if the damage is irrelevant
        // to us (not PvP/PvE, no valid victim, self-damage, etc.).
        public static DamageContext? Resolve(Entity victimEntity, DamageSource? damageSource)
        {
            var system = CombatLogSystemAccessor.Instance;
            if (system == null || damageSource == null) return null;

            bool isPvP = damageSource.Source == EnumDamageSource.Player;
            bool isPvE = damageSource.Source == EnumDamageSource.Entity;
            if (!isPvP && !isPvE) return null;

            var victimEp = victimEntity as EntityPlayer;
            var victim = victimEp?.Player as IServerPlayer;
            if (victimEp == null || victim == null) return null;

            var ctx = new DamageContext
            {
                Victim = victim,
                VictimEntity = victimEp,
                IsPvP = isPvP,
                IsPvE = isPvE,
            };

            // Resolve the attacker (only meaningful for PvP).
            if (isPvP)
            {
                var attackerEp = damageSource.GetCauseEntity() as EntityPlayer;
                var attacker = attackerEp?.Player as IServerPlayer;
                if (attacker == null || attacker == victim) return null;

                ctx.Attacker = attacker;
                ctx.AttackerEntity = attackerEp;
            }

            // Resolve claims for both sides.
            var victimClaims = system.GetClaimsAt(victimEp.Pos.AsBlockPos);
            ctx.VictimClaim = victimClaims?.Length > 0 ? victimClaims[0] : null;

            if (ctx.AttackerEntity != null)
            {
                var attackerClaims = system.GetClaimsAt(ctx.AttackerEntity.Pos.AsBlockPos);
                ctx.AttackerClaim = attackerClaims?.Length > 0 ? attackerClaims[0] : null;
            }

            // Combat state is only tracked for players.
            if (ctx.Attacker != null)
            {
                ctx.AttackerInCombat = system.IsInCombat(ctx.Attacker);
                ctx.VictimInCombat = system.IsInCombat(victim);
            }

            return ctx;
        }
    }
}