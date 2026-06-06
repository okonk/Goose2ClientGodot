using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class CastPacket : PacketHandler
    {
        public int LoginId { get; set; }

        public override string Prefix { get; } = "CST";

        public override object Parse(PacketParser p)
        {
            return new CastPacket()
            {
                LoginId = p.GetInt32(),
            };
        }
    }
}
