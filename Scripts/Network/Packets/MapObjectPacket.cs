using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class MapObjectPacket : PacketHandler
    {
        public int GraphicId { get; set; }
        public int GraphicFile { get; set; }
        public int SoundId { get; set; }
        public int SoundFile { get; set; }
        public int TileX { get; set; }
        public int TileY { get; set; }
        public string Title { get; set; }
        public string Name { get; set; }
        public string Surname { get; set; }
        public int StackSize { get; set; }
        public ItemMaterial MaterialType { get; set; }
        public ItemFlags Flags { get; set; }
        public int DropAnimation { get; set; }
        public int GraphicR { get; set; }
        public int GraphicG { get; set; }
        public int GraphicB { get; set; }
        public int GraphicA { get; set; }

        public override string Prefix { get; } = "DOB";

        public override object Parse(PacketParser p)
        {
            var packet = new MapObjectPacket()
            {
                GraphicId = p.GetInt32(),
                GraphicFile = p.GetInt32(),
                SoundId = p.GetInt32(),
                SoundFile = p.GetInt32(),
                TileX = p.GetInt32() - 1,
                TileY = p.GetInt32() - 1,
                Title = p.GetString(),
                Name = p.GetString(),
                Surname = p.GetString(),
                StackSize = p.GetInt32(),
                MaterialType = (ItemMaterial)p.GetInt32(),
                Flags = (ItemFlags)p.GetInt32(),
                DropAnimation = p.GetInt32(),
            };

            if (p.Peek() == '*')
            {
                p.GetString();
                packet.GraphicR = 0;
                packet.GraphicG = 0;
                packet.GraphicB = 0;
                packet.GraphicA = 0;
            }
            else
            {
                packet.GraphicR = p.GetInt32();
                packet.GraphicG = p.GetInt32();
                packet.GraphicB = p.GetInt32();
                packet.GraphicA = p.GetInt32();
            }

            return packet;
        }
    }
}
