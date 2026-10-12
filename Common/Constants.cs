namespace warwthtreason.Common
{
    public static class Constants
    {
        // --- Config ---
        public const string ConfigDir = "WarWithoutReason";
        public const string ConfigMain = "WarWithoutReason.json";
        public const string ConfigClaimFlags = "WWR_ClaimFlags.json";
        public const string ConfigPending = "WWR_PendingDeaths.json";
        public const string ConfigStats = "WWR_PlayerStats.json";

        // --- Network ---
        public const string NetworkChannel = "warwthtreason";

        // --- Harmony ---
        public const string HarmonyId = "com.warwthtreason.patch";

        // --- UI ---
        public const string ScreenMessageKey = "combat_status";

        // --- Timings (milliseconds) ---
        // How often the stats manager checks whether the dirty flag
        // should be flushed to disk. Cheap check, no I/O on no-op.
        public const int StatsFlushCheckMs = 5000;

        // Minimum interval between actual writes. Batches many small
        // updates into one disk write. Trades a bit of durability for
        // not hammering the disk on every kill.
        public const int StatsFlushIntervalMs = 30_000;
    }
}