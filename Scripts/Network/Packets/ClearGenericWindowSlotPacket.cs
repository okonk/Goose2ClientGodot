using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class ClearGenericWindowSlotPacket : PacketHandler
    {
        public int WindowId { get; set; }
        public int SlotNumber { get; set; }

        public override string Prefix { get; } = "GWC";

        public override object Parse(PacketParser p)
        {
            return new ClearGenericWindowSlotPacket()
            {
                WindowId = p.GetInt32(),
                SlotNumber = p.GetInt32() - 1
            };
        }
    }
}
