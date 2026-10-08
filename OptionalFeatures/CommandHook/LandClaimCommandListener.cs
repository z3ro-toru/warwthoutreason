/*using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using CommandHook;

namespace warwthtreason.Server
{
    // CommandHook listener for land claim commands. This allows us to intercept the /land claim allowpvp and /land claim allowpve commands
    public class LandClaimCommandListener : CommandHook.ICommandListener
    {
        private readonly CombatLogSystem system;

        public LandClaimCommandListener(CombatLogSystem system)
        {
            this.system = system;
        }

        // List of commands we listen to. CommandHook matches them by full path.
        public string[] Commands => new[]
        {
            "land claim allowpvp",
            "land claim allowpve"
        };

        // Before is called before the command is executed. We can return a TextCommandResult to cancel the command and send a message to the player.
        public TextCommandResult? Before(TextCommandCallingArgs args)
        {
            string command = args.Command;

            if (command == "land claim allowpvp")
                return HandleToggle(args, isPvP: true);
            if (command == "land claim allowpve")
                return HandleToggle(args, isPvP: false);

            return null;
        }

        public void After(TextCommandCallingArgs args)
        {
            // not used.
        }

        // Main logic for handling the toggle of PvP/PvE flags in land claims. 
        // This checks if the player is in a claim, has access, and toggles the flag accordingly.
        private TextCommandResult HandleToggle(TextCommandCallingArgs args, bool isPvP)
        {
            if (args.Caller.Player is not IServerPlayer player)
                return TextCommandResult.Error("Команда доступна только игрокам.");

            var claims = system.Sapi.World.Claims.Get(player.Entity.Pos.AsBlockPos);
            if (claims == null || claims.Length == 0)
                return TextCommandResult.Error("Вы не находитесь в привате.");

            var claim = claims[0];

            bool hasAccess = system.Sapi.World.Claims.TryAccess(
                player, player.Entity.Pos.AsBlockPos, EnumBlockAccessFlags.BuildOrBreak);

            if (!hasAccess)
                return TextCommandResult.Error("У вас нет прав на управление этим приватом.");

            // swiitching the flag. false - disabling, true - enabling. default - true.
            string claimId = system.GetClaimId(claim);
            var flags = system.GetClaimFlags(claim);

            bool currentValue = isPvP
                ? (flags?.AllowPvP ?? system.Config.PreventPvPInClaims ? false : true)
                : (flags?.AllowPvE ?? system.Config.PreventPvEInClaims ? false : true);

            // Current value inverting. We can't directly write to claimFlagsConfig from here,
            // therefore, we delegate this through the public method of the system.
            return system.ToggleClaimFlagDirect(player, isPvP, !currentValue);
        }
    }
}*/