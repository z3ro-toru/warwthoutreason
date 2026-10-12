using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

using warwthtreason.Common;

namespace warwthtreason.Server.PlayerStats
{
    // Tracks per-player PvP statistics and flushes them to disk periodically.
    // No gameplay restrictions are imposed yet — this is data collection only.
    public class PlayerStatsManager
    {
        private readonly ICoreServerAPI sapi;
        private readonly PlayerStatsConfig store;

        // Save at most once every 30 seconds. In-memory updates are immediate.
        private long lastFlushMs;
        private bool dirty;

        public PlayerStatsManager(ICoreServerAPI sapi)
        {
            this.sapi = sapi;

            string path = $"{Constants.ConfigDir}/{Constants.ConfigStats}";
            store = sapi.LoadModConfig<PlayerStatsConfig>(path) ?? new PlayerStatsConfig();
            sapi.StoreModConfig(store, path);

            sapi.Event.RegisterGameTickListener(_ => MaybeFlush(), Constants.StatsFlushCheckMs);
        }

        // Called on PlayerJoin. Refreshes name and last-seen timestamp.
        public void OnPlayerJoin(IServerPlayer player)
        {
            if (player == null) return;
            var stats = GetOrCreate(player.PlayerUID);
            stats.LastKnownName = player.PlayerName ?? "unknown";
            stats.LastSeenUtc = System.DateTime.UtcNow;
            dirty = true;
        }

        // Called when a player damages another player.
        // attackerWasInCombat / victimWasInCombat are pre-hit snapshots.
        public void OnPlayerDamaged(
            IServerPlayer attacker,
            IServerPlayer victim,
            bool attackerWasInCombat,
            bool victimWasInCombat)
        {
            // Initiation = a hit that started a fresh fight, not a continuation.
            if (!attackerWasInCombat && !victimWasInCombat)
            {
                GetOrCreate(attacker.PlayerUID).PvPInitiations++;
                dirty = true;
            }
        }

        // Called on any entity death. Only player-vs-player deaths count.
        public void OnEntityDeath(Entity entity, DamageSource damageSource)
        {
            if (entity is not EntityPlayer victimEntity) return;
            if (victimEntity.Player is not IServerPlayer victim) return;
            if (damageSource?.Source != EnumDamageSource.Player) return;

            var killerEntity = damageSource.GetCauseEntity() as EntityPlayer;
            if (killerEntity?.Player is not IServerPlayer killer) return;
            if (killer == victim) return;

            GetOrCreate(killer.PlayerUID).PvPKills++;
            GetOrCreate(victim.PlayerUID).PvPDeaths++;
            dirty = true;
        }

        // Called from Dispose — flushes any pending changes.
        public void Flush()
        {
            if (!dirty) return;
            sapi.StoreModConfig(store, "WarWithoutReason/WWR_PlayerStats.json");
            dirty = false;
            lastFlushMs = sapi.World.ElapsedMilliseconds;
        }

        private void MaybeFlush()
        {
            if (!dirty) return;
            long now = sapi.World.ElapsedMilliseconds;
            if (now - lastFlushMs < Constants.StatsFlushIntervalMs) return;
            Flush();
        }

        private PlayerStats GetOrCreate(string uid)
        {
            if (!store.Stats.TryGetValue(uid, out var stats))
            {
                stats = new PlayerStats();
                store.Stats[uid] = stats;
            }
            return stats;
        }
    }
}