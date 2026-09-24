using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class PartyBuffClearPacket : PacketHandler
    {
        public int LoginId { get; set; }

        public override string Prefix { get; } = "PBC";

        public override object Parse(PacketParser p)
        {
            return new PartyBuffClearPacket()
            {
                LoginId = p.GetInt32()
            };
        }
    }
}
