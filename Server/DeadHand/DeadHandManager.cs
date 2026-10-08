using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace warwthtreason.Server.DeadHand
{
    // Handles the combat-log punishment (aka "Karma" / "Dead Hand").
    //
    // When a player logs out while flagged as in-combat, we schedule them
    // for a kill on their next login. The actual kill is deferred until
    // rejoin, because at PlayerLeave the entity is already being despawned
    // and calling Die() then leads to a race condition — vanilla then shows
    // "killed by another player" instead of our custom message.
    public class DeadHandManager
    {
        private readonly ICoreServerAPI sapi;
        private readonly CombatLogConfig config;

        // Persistent list of UIDs scheduled for a kill on next login.
        private readonly PendingDeaths pendingDeaths;

        // Caches player names by UID, so we can still display the name
        // after the player leaves and PlayerName has been cleared.
        private readonly Dictionary<string, string> playerNames = new();

        public DeadHandManager(ICoreServerAPI sapi, CombatLogConfig config)
        {
            this.sapi = sapi;
            this.config = config;

            pendingDeaths = sapi.LoadModConfig<PendingDeaths>("WWR_PendingDeaths.json")
                ?? new PendingDeaths();
            sapi.StoreModConfig(pendingDeaths, "WWR_PendingDeaths.json");
        }

        // Called from CombatLogSystem.OnPlayerJoin.
        public void OnPlayerJoin(IServerPlayer player)
        {
            if (player == null) return;

            // Cache the name while it's still valid — PlayerName may become
            // null after PlayerLeave fires and the network session closes.
            playerNames[player.PlayerUID] = player.PlayerName ?? "unknown";

            // If the player was flagged for a combat-log punishment, kill
            // them after a short delay to let the entity fully load into the world.
            if (pendingDeaths.PlayerUids.Contains(player.PlayerUID))
            {
                pendingDeaths.PlayerUids.Remove(player.PlayerUID);
                sapi.StoreModConfig(pendingDeaths, "WWR_PendingDeaths.json");

                // 500ms = one tick. At PlayerJoin the entity isn't fully
                // registered yet; Pos and health behavior may be missing.
                sapi.Event.RegisterCallback(_ =>
                {
                    if (player.Entity != null && player.Entity.Alive)
                    {
                        Kill(player);
                    }
                }, 500);
            }
        }

        // Called from CombatLogSystem.OnPlayerLeave.
        // wasInCombat is computed by the caller because combatTimers lives
        // in CombatLogSystem — no point duplicating that state here.
        public void OnPlayerLeave(IServerPlayer player, bool wasInCombat)
        {
            if (player == null) return;
            if (!wasInCombat) return;

            if (!config.KillOnCombatLogout)
            {
                sapi.Logger.Notification(
                    $"[WarWithoutReason] Player '{player.PlayerName}' ({player.PlayerUID}) " +
                    $"logged out during combat. Punishment skipped (KillOnCombatLogout = false).");
                return;
            }

            // Mark the player for a kill on next login.
            if (!pendingDeaths.PlayerUids.Contains(player.PlayerUID))
            {
                pendingDeaths.PlayerUids.Add(player.PlayerUID);
                sapi.StoreModConfig(pendingDeaths, "WWR_PendingDeaths.json");
            }

            sapi.Logger.Notification(
                $"[WarWithoutReason] Player '{player.PlayerName}' ({player.PlayerUID}) " +
                $"logged out during combat. Marked for kill on next login.");
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
                $"logged out during combat and was killed on rejoin.");

            // Broadcast to all online players in their own language.
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