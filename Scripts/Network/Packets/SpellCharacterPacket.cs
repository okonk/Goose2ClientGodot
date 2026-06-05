using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class SpellCharacterPacket : PacketHandler
    {
        public int LoginId { get; set; }

        public int AnimationId { get; set; }

        public override string Prefix { get; } = "SPP";

        public override object Parse(PacketParser p)
        {
            return new SpellCharacterPacket()
            {
                LoginId = p.GetInt32(),
                AnimationId = p.GetInt32()
            };
        }
    }
}
