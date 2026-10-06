using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
//using CommandHook;

namespace warwthtreason.Server
{
    // Слушатель CommandHook. Реализация интерфейса ICommandListener
    // требует наличия методов Before и After.
    public class LandClaimCommandListener : CommandHook.ICommandListener
    {
        private readonly CombatLogSystem system;

        public LandClaimCommandListener(CombatLogSystem system)
        {
            this.system = system;
        }

        // Список команд, которые мы слушаем. CommandHook матчит их по полному пути.
        public string[] Commands => new[]
        {
            "land claim allowpvp",
            "land claim allowpve"
        };

        // Before вызывается до оригинальной команды. Возврат не-null отменяет её выполнение.
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
            // Не используется.
        }

        // Общая логика переключения флага. Копия логики ToggleClaimFlag,
        // но вызывается из CommandHook, а не из команды /claimflag.
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

            // Переключаем флаг: on → off, off → on, default → on.
            string claimId = system.GetClaimId(claim);
            var flags = system.GetClaimFlags(claim);

            bool currentValue = isPvP
                ? (flags?.AllowPvP ?? system.Config.PreventPvPInClaims ? false : true)
                : (flags?.AllowPvE ?? system.Config.PreventPvEInClaims ? false : true);

            // Инвертируем текущее значение. Мы не можем напрямую писать в claimFlagsConfig отсюда,
            // поэтому делегируем это через публичный метод системы.
            return system.ToggleClaimFlagDirect(player, isPvP, !currentValue);
        }
    }
}