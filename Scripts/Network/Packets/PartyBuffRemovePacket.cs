using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class PartyBuffRemovePacket : PacketHandler
    {
        public int LoginId { get; set; }
        public int EffectId { get; set; }

        public override string Prefix { get; } = "PBR";

        public override object Parse(PacketParser p)
        {
            return new PartyBuffRemovePacket()
            {
                LoginId = p.GetInt32(),
                EffectId = p.GetInt32()
            };
        }
    }
}
