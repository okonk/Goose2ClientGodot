using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class GenericWindowSlotPacket : InventorySlotPacket
    {
        public int WindowId { get; set; }

        public override string Prefix { get; } = "GWS";

        public override object Parse(PacketParser p)
        {
            p.Delimeter = '|';

            var pkt = new GenericWindowSlotPacket();
            pkt.WindowId = p.GetInt32();
            ReadFieldsInto(p, pkt);
            return pkt;
        }
    }
}
