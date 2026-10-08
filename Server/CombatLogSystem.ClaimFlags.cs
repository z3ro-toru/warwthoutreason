using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace warwthtreason.Server
{
    // Claim-flag logic: reading/writing per-claim PvP and PvE settings,
    // access checks, and the /claimflag command tree.
    public partial class CombatLogSystem
    {
        // === Command registration ===

        private void RegisterClaimFlagCommands()
        {
            sapi.ChatCommands.Create("claimflag")
                .WithDescription(Lang.Get("warwthtreason:cmd-claimflag-desc"))
                .RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("pvp")
                    .WithDescription(Lang.Get("warwthtreason:cmd-claimflag-pvp-desc"))
                    .RequiresPrivilege(Privilege.chat)
                    .WithArgs(sapi.ChatCommands.Parsers.Word("value"))
                    .HandleWith(args => ToggleClaimFlag(args, isPvP: true))
                .EndSubCommand()
                .BeginSubCommand("pve")
                    .WithDescription(Lang.Get("warwthtreason:cmd-claimflag-pve-desc"))
                    .RequiresPrivilege(Privilege.chat)
                    .WithArgs(sapi.ChatCommands.Parsers.Word("value"))
                    .HandleWith(args => ToggleClaimFlag(args, isPvP: false))
                .EndSubCommand()
                .BeginSubCommand("status")
                    .WithDescription(Lang.Get("warwthtreason:cmd-claimflag-status-desc"))
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(OnShowClaimStatus)
                .EndSubCommand();
        }

        // === Public helpers for patches ===

        public LandClaim[]? GetClaimsAt(BlockPos pos) => sapi.World.Claims.Get(pos);

        // Returns a unique string ID for a claim.
        // Uses center coordinates because LandClaim has no public Index/Id,
        // and claims cannot overlap, so centers are guaranteed unique.
        public string GetClaimId(LandClaim claim)
        {
            var center = claim.Center;
            return $"{center.X},{center.Y},{center.Z}";
        }

        // Returns per-claim flags, or null if no flags are set for this claim.
        public ClaimFlags? GetClaimFlags(LandClaim claim)
        {
            if (claim == null) return null;
            string id = GetClaimId(claim);
            return claimFlagsConfig.Flags.TryGetValue(id, out var flags) ? flags : null;
        }

        // === Access control ===

        private bool CanManageClaimFlags(IServerPlayer player)
        {
            if (config.AllowPlayersToManageClaimFlags) return true;

            // Admins always pass so they can still fix individual claims.
            return player.HasPrivilege(Privilege.root);
        }

        // === Command handlers ===

        private TextCommandResult ToggleClaimFlag(TextCommandCallingArgs args, bool isPvP)
        {
            if (args.Caller.Player is not IServerPlayer player)
                return TextCommandResult.Error(Lang.Get("warwthtreason:err-players-only"));

            if (!CanManageClaimFlags(player))
                return TextCommandResult.Error(Lang.GetL(player.LanguageCode, "warwthtreason:err-flag-management-disabled"));

            var claims = sapi.World.Claims.Get(player.Entity.Pos.AsBlockPos);
            if (claims == null || claims.Length == 0)
                return TextCommandResult.Error(Lang.GetL(player.LanguageCode, "warwthtreason:err-not-in-claim"));

            var claim = claims[0];

            // TryAccess returns true for the claim owner and any player with
            // build rights. Guests with Traverse/Use cannot change flags.
            bool hasAccess = sapi.World.Claims.TryAccess(
                player, player.Entity.Pos.AsBlockPos, EnumBlockAccessFlags.BuildOrBreak);

            if (!hasAccess)
                return TextCommandResult.Error(Lang.GetL(player.LanguageCode, "warwthtreason:err-no-permission"));

            string claimId = GetClaimId(claim);
            if (!claimFlagsConfig.Flags.TryGetValue(claimId, out var flags))
            {
                flags = new ClaimFlags();
                claimFlagsConfig.Flags[claimId] = flags;
            }

            string value = (args[0] as string ?? "").ToLowerInvariant();
            bool? newValue;
            switch (value)
            {
                case "on": newValue = true; break;
                case "off": newValue = false; break;
                case "default": newValue = null; break;
                default:
                    return TextCommandResult.Error(Lang.GetL(player.LanguageCode, "warwthtreason:err-invalid-value"));
            }

            if (isPvP) flags.AllowPvP = newValue;
            else flags.AllowPvE = newValue;

            sapi.StoreModConfig(claimFlagsConfig, "WWR_ClaimFlags.json");

            string flagName = isPvP ? "PvP" : "PvE";

            if (value == "default")
            {
                return TextCommandResult.Success(
                    Lang.GetL(player.LanguageCode, "warwthtreason:flag-reset", flagName, claimId));
            }

            return TextCommandResult.Success(
                Lang.GetL(player.LanguageCode, "warwthtreason:flag-set", flagName, claimId, value));
        }

        private TextCommandResult OnShowClaimStatus(TextCommandCallingArgs args)
        {
            if (args.Caller.Player is not IServerPlayer player)
                return TextCommandResult.Error(Lang.Get("warwthtreason:err-players-only"));

            var claims = sapi.World.Claims.Get(player.Entity.Pos.AsBlockPos);
            if (claims == null || claims.Length == 0)
                return TextCommandResult.Error(Lang.GetL(player.LanguageCode, "warwthtreason:err-not-in-claim"));

            string claimId = GetClaimId(claims[0]);

            if (!claimFlagsConfig.Flags.TryGetValue(claimId, out var flags))
            {
                return TextCommandResult.Success(
                    Lang.GetL(player.LanguageCode, "warwthtreason:claim-status-empty", claimId));
            }

            string defaultValue = Lang.GetL(player.LanguageCode, "warwthtreason:value-default");
            string pvp = flags.AllowPvP?.ToString() ?? defaultValue;
            string pve = flags.AllowPvE?.ToString() ?? defaultValue;

            return TextCommandResult.Success(
                Lang.GetL(player.LanguageCode, "warwthtreason:claim-status-full", claimId, pvp, pve));
        }
    }
}