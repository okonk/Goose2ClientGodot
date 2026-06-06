using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class MoveCharacterPacket : PacketHandler
    {
        public int LoginId { get; set; }

        public int MapX { get; set; }

        public int MapY { get; set; }

        public override string Prefix { get; } = "MOC";

        public override object Parse(PacketParser p)
        {
            return new MoveCharacterPacket()
            {
                LoginId = p.GetInt32(),
                MapX = p.GetInt32() - 1,
                MapY = p.GetInt32() - 1
            };
        }
    }
}
