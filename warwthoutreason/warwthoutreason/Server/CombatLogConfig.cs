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

        // Combat-in-claim policy. Evaluated in this order:
        //
        //  (always) Mutual: if both players are already in combat,
        //                   damage passes. This is the chase mechanic.
        //
        // If true: a player standing in THEIR OWN claim may initiate
        //          combat against a target that is already in combat.
        //          The attacker enters combat as a consequence.
        public bool AllowOwnerDefenseInClaims { get; set; } = true;

        // If true: a player standing in ANY claim (own or foreign) may
        //          initiate combat against a target that is already in
        //          combat. WARNING: enables third-party intervention
        //          but also third-party ganking. Off by default.
        public bool AllowOpenDefenseInClaims { get; set; } = false;


        // If true  — players can use /claimflag in their own claims.
        // If false — only admins  can change claim flags.
        // This lets server owners centralize claim protection when needed.
        public bool AllowPlayersToManageClaimFlags { get; set; } = true;

        
    }
}