// The packet transmits to the client how many seconds are left before exiting the battle.
// The [ProtoContract] attribute is required — marks the class as a serializable ProtoBuf.
// [ProtoMember(1)] is the ordinal number of the field. DO NOT CHANGE THE NUMBERS OF EXISTING FIELDS,
// otherwise the client and server will cease to understand each other.

using ProtoBuf;

namespace warwthtreason.Common
{    
    [ProtoContract]
    public class CombatTimerPacket
    {        
        [ProtoMember(1)]
        public int RemainingSeconds { get; set; }
    }
}