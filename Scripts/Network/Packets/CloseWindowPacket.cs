using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class CloseWindowPacket : PacketHandler
    {
        public int WindowId { get; set; }

        public override string Prefix { get; } = "CLW";

        public override object Parse(PacketParser p)
        {
            return new CloseWindowPacket()
            {
                WindowId = p.GetInt32(),
            };
        }
    }
}
