// Config file. Look in ModConfig/WarWithoutReason.json.

namespace warwthtreason.Server
{
    
    public class CombatLogConfig
    {
        // Duration of the combat mode in seconds. Default is 30 seconds.
        public int CombatDurationSeconds { get; set; } = 30;

        // Whether to send messages to the chat when entering/exiting combat.
        public bool SendChatMessages { get; set; } = true;

        // Whether to show large notifications on the screen.
        public bool ShowScreenMessages { get; set; } = true;

        // Whether to kill the player who logs out during combat.
        public bool KillOnCombatLogout { get; set; } = true;

        // Enables in-built claim protection system. Automatically disabled when a SafeZone is detected.
        public bool EnableClaimProtection { get; set; } = true;

        // Global ban on PvP in claims. Overridden by the per-claim flag AllowPvP.
        public bool PreventPvPInClaims { get; set; } = true;

        // Global ban on PvE (damage from mobs) in claims. Disabled by default to allow mobs to attack as usual.
        public bool PreventPvEInClaims { get; set; } = false;
    }
}