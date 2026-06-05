using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class ClassUpdatePacket : PacketHandler
    {
        public int ClassId { get; set; }
        public string Name { get; set; }

        public override string Prefix { get; } = "CUP";

        public override object Parse(PacketParser p)
        {
            return new ClassUpdatePacket()
            {
                ClassId = p.GetInt32(),
                Name = p.GetString()
            };
        }
    }
}
