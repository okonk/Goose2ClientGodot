using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class TileUpdatePacket : PacketHandler
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int[] Tiles { get; set; }
        public int Flags { get; set; }

        public override string Prefix { get; } = "TUP";

        public override object Parse(PacketParser p)
        {
            var packet = new TileUpdatePacket()
            {
                X = p.GetInt32() - 1,
                Y = p.GetInt32() - 1,
                Tiles = new int[10],
            };

            for (int layer = 0; layer < 5; layer++)
            {
                packet.Tiles[layer * 2] = p.GetInt32();
                packet.Tiles[layer * 2 + 1] = p.GetInt32();
            }

            packet.Flags = p.GetInt32();

            return packet;
        }
    }
}
