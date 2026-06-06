using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class ClearInventorySlotPacket : PacketHandler
    {
        public int SlotNumber { get; set; }

        public override string Prefix { get; } = "CIS";

        public override object Parse(PacketParser p)
        {
            return new ClearInventorySlotPacket()
            {
                SlotNumber = p.GetInt32() - 1
            };
        }
    }
}
