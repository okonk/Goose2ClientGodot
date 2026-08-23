using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class SeeInvisiblePacket : PacketHandler
    {
        public bool CanSee { get; set; }

        public override string Prefix { get; } = "SINVS";

        public override object Parse(PacketParser p)
        {
            return new SeeInvisiblePacket()
            {
                CanSee = p.GetInt32() == 1
            };
        }
    }
}
