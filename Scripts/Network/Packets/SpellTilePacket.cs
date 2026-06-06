using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class SpellTilePacket : PacketHandler
    {
        public int TileX { get; set; }

        public int TileY { get; set; }

        public int AnimationId { get; set; }

        public override string Prefix { get; } = "SPA";

        public override object Parse(PacketParser p)
        {
            return new SpellTilePacket()
            {
                TileX = p.GetInt32() - 1,
                TileY = p.GetInt32() - 1,
                AnimationId = p.GetInt32()
            };
        }
    }
}
