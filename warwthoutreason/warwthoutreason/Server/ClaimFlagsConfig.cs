// Config for per-claim flags. Look in ModConfig/WWRClaimFlags.json
// This allows server admins and players to set PvP/PvE permissions for specific land claims.
// The key is a unique ID for the claim (generated from corner coordinates), and the value is a set of flags for that claim.
// The flags can be true (allowed), false (disallowed), or null (use global setting). Same for PvE.
// This is used in conjunction with the CombatLogSystem to determine if combat is allowed in a claim.

using System.Collections.Generic;

namespace warwthtreason.Server
{
    // 
    public class ClaimFlagsConfig
    {        
        public Dictionary<string, ClaimFlags> Flags { get; set; } = [];
    }
        
    public class ClaimFlags
    {
        public bool? AllowPvP { get; set; } = null;
                
        public bool? AllowPvE { get; set; } = null;
    }
}