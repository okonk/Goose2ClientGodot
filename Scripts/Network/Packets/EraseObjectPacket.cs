using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class EraseObjectPacket : PacketHandler
    {
        public int TileX { get; set; }

        public int TileY { get; set; }

        public override string Prefix { get; } = "EOB";

        public override object Parse(PacketParser p)
        {
            return new EraseObjectPacket()
            {
                TileX = p.GetInt32() - 1,
                TileY = p.GetInt32() - 1
            };
        }
    }
}
