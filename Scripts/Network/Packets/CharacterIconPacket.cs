using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class CharacterIconPacket : PacketHandler
    {
        public int LoginId { get; set; }
        public int Sheet { get; set; }
        public int Graphic { get; set; }

        public override string Prefix { get; } = "CHI";

        public override object Parse(PacketParser p)
        {
            return new CharacterIconPacket()
            {
                LoginId = p.GetInt32(),
                Sheet = p.GetInt32(),
                Graphic = p.GetInt32()
            };
        }
    }
}
