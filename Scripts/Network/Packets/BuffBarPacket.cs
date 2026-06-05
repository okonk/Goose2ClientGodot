using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class BuffBarPacket : PacketHandler
    {
        public int SlotNumber { get; set; }
        public int GraphicId { get; set; }
        public int GraphicFile { get; set; }
        public string Name { get; set; }

        public override string Prefix { get; } = "BUF";

        public override object Parse(PacketParser p)
        {
            var packet = new BuffBarPacket()
            {
                SlotNumber = p.GetInt32() - 1
            };

            if (p.LengthRemaining() > 0)
            {
                packet.GraphicId = p.GetInt32();
                packet.GraphicFile = p.GetInt32();
                packet.Name = p.GetString();
            }

            return packet;
        }
    }
}
