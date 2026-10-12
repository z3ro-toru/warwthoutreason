using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

using warwthtreason.Common;

namespace warwthtreason.Server.DeadHand
{
    // Handles the combat-logout punishment.
    //
    // Two policies:
    //   KeepTimer — restore the remaining combat timer on next login. No kill.
    //   Kill      — kill the player on next login. Legacy behavior.
    //
    // Both defer the actual action until rejoin, because at PlayerLeave the
    // entity is already being despawned and calling Die() then leads to a
    // race condition where vanilla shows "killed by another player" instead
    // of our custom message.
    public class DeadHandManager
    {
        private readonly ICoreServerAPI sapi;
        private readonly CombatLogConfig config;
        private readonly Action<IServerPlayer, int> restoreCombatTimer;

        private readonly PendingDeaths pendingDeaths;
        private readonly Dictionary<string, string> playerNames = [];

        public DeadHandManager(
    ICoreServerAPI sapi,
    CombatLogConfig config,
    Action<IServerPlayer, int> restoreCombatTimer)
        {
            this.sapi = sapi;
            this.config = config;
            this.restoreCombatTimer = restoreCombatTimer;

            string path = $"{Constants.ConfigDir}/{Constants.ConfigPending}";
            pendingDeaths = sapi.LoadModConfig<PendingDeaths>(path) ?? new PendingDeaths();
            sapi.StoreModConfig(pendingDeaths, path);
        }

        // Called from CombatLogSystem.OnPlayerJoin.
        public void OnPlayerJoin(IServerPlayer player)
        {
            if (player == null) return;

            // Cache the name while it's still valid — PlayerName may become
            // null after PlayerLeave fires and the network session closes.
            playerNames[player.PlayerUID] = player.PlayerName ?? "unknown";

            if (!pendingDeaths.PendingCombat.TryGetValue(player.PlayerUID, out int remainingSeconds))
                return;

            pendingDeaths.PendingCombat.Remove(player.PlayerUID);
            sapi.StoreModConfig(pendingDeaths, "WWR_PendingDeaths.json");

            // Defer one tick — at PlayerJoin the entity isn't fully registered
            // yet; Pos and health behavior may be missing.
            sapi.Event.RegisterCallback(_ =>
            {
                if (player.Entity == null || !player.Entity.Alive) return;

                switch (config.CombatLogoutPolicy)
                {
                    case CombatLogoutPolicy.KeepTimer:
                        restoreCombatTimer(player, remainingSeconds);
                        break;

                    case CombatLogoutPolicy.Kill:
                        Kill(player);
                        break;
                }
            }, 500);
        }

        // Called from CombatLogSystem.OnPlayerLeave.
        // remainingSeconds is the combat time left at logout, in seconds.
        public void OnPlayerLeave(IServerPlayer player, bool wasInCombat, int remainingSeconds)
        {
            if (player == null) return;
            if (!wasInCombat) return;

            // Nothing meaningful to restore if the timer already expired.
            if (remainingSeconds <= 0) return;

            if (pendingDeaths.PendingCombat.TryGetValue(player.PlayerUID, out int stored))
            {
                // Should not happen under normal flow, but if it does,
                // keep the longer remaining time.
                if (remainingSeconds > stored)
                    pendingDeaths.PendingCombat[player.PlayerUID] = remainingSeconds;
            }
            else
            {
                pendingDeaths.PendingCombat[player.PlayerUID] = remainingSeconds;
            }

            sapi.StoreModConfig(pendingDeaths, "WWR_PendingDeaths.json");

            sapi.Logger.Notification(
                $"[WarWithoutReason] Player '{player.PlayerName}' ({player.PlayerUID}) " +
                $"logged out during combat with {remainingSeconds}s remaining. " +
                $"Policy: {config.CombatLogoutPolicy}.");
        }

        // Kills the player with a Suicide DamageSource, so the game attributes
        // the death correctly and doesn't blame the last opponent.
        private void Kill(IServerPlayer player)
        {
            var entity = player.Entity;
            if (entity == null || !entity.Alive) return;

            string playerName = playerNames.TryGetValue(player.PlayerUID, out var cached)
                ? cached
                : player.PlayerName ?? "unknown";

            var damageSource = new DamageSource
            {
                Source = EnumDamageSource.Suicide,
                Type = EnumDamageType.Injury,
                SourceEntity = entity,
                CauseEntity = entity
            };

            entity.Die(EnumDespawnReason.Death, damageSource);

            sapi.Logger.Notification(
                $"[WarWithoutReason] Player '{playerName}' ({player.PlayerUID}) " +
                $"killed on rejoin (CombatLogoutPolicy = Kill).");

            foreach (var onlinePlayer in sapi.World.AllOnlinePlayers)
            {
                if (onlinePlayer is IServerPlayer sp)
                {
                    string message = Lang.GetL(sp.LanguageCode, "warwthtreason:combat-log-kill", playerName);
                    sp.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
                }
            }
        }
    }
}