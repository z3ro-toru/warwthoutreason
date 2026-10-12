using System;
using System.Collections.Generic;

namespace warwthtreason.Server.PlayerStats
{
    // Persisted stats for all players. Serialised to
    // ModConfig/WarWithoutReason/WWR_PlayerStats.json.
    // Key: PlayerUID.
    public class PlayerStatsConfig
    {
        public Dictionary<string, PlayerStats> Stats { get; set; } = new();
    }

    // Per-player counters. All values are non-negative.
    public class PlayerStats
    {
        // Last display name seen for this UID, so admins can identify entries.
        public string LastKnownName { get; set; } = "";

        // Number of distinct fights this player started. A fight is
        // "started" when they hit a target that wasn't already in combat.
        public int PvPInitiations { get; set; }

        // Kills landed on other players.
        public int PvPKills { get; set; }

        // Deaths caused by other players.
        public int PvPDeaths { get; set; }

        // UTC timestamp of the last login. Used for future cleanup.
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }
}