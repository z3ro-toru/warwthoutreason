using System.Collections.Generic;

namespace warwthtreason.Server.DeadHand
{
    // Persisted state for the Dead Hand system.
    // Key: PlayerUID. Value: seconds of combat remaining at logout time.
    // Stored so the punishment survives a server restart?
    public class PendingDeaths
    {
        public Dictionary<string, int> PendingCombat { get; set; } = [];
    }
}