using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class ChangeHeadingPacket : PacketHandler
    {
        public int LoginId { get; set; }

        public Direction Direction { get; set; }

        public override string Prefix { get; } = "CHH";

        public override object Parse(PacketParser p)
        {
            return new ChangeHeadingPacket()
            {
                LoginId = p.GetInt32(),
                Direction = (Direction)(p.GetInt32() - 1)
            };
        }
    }
}
