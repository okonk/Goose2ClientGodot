using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class ClearBankSlotPacket : PacketHandler
    {
        public int SlotNumber { get; set; }

        public override string Prefix { get; } = "CBS";

        public override object Parse(PacketParser p)
        {
            return new ClearBankSlotPacket()
            {
                SlotNumber = p.GetInt32() - 1
            };
        }
    }
}
