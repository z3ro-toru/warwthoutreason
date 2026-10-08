using System.Collections.Generic;

namespace warwthtreason.Server.DeadHand
{
    // Stores UIDs of players who logged out during combat and are
    // scheduled to be killed on their next login. Persisted to disk
    // so the punishment survives a server restart.
    public class PendingDeaths
    {
        public List<string> PlayerUids { get; set; } = new();
    }
}