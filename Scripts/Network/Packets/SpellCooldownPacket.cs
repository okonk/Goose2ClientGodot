using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class SpellCooldownPacket : PacketHandler
    {
        public int SlotNumber { get; set; }

        public long RemainingMilliseconds { get; set; }

        public override string Prefix { get; } = "CDR";

        public override object Parse(PacketParser p)
        {
            return new SpellCooldownPacket()
            {
                SlotNumber = p.GetInt32() - 1,
                RemainingMilliseconds = p.GetInt64()
            };
        }
    }
}
