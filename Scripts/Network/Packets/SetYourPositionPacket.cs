using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class SetYourPositionPacket : PacketHandler
    {
        public int MapX { get; set; }

        public int MapY { get; set; }

        public override string Prefix { get; } = "SUP";

        public override object Parse(PacketParser p)
        {
            return new SetYourPositionPacket()
            {
                MapX = p.GetInt32() - 1,
                MapY = p.GetInt32() - 1
            };
        }
    }
}
