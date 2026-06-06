using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class VitalsPercentagePacket : PacketHandler
    {
        public int LoginId { get; set; }

        public float HPPercentage { get; set; }

        public float MPPercentage { get; set; }

        public override string Prefix { get; } = "VPU";

        public override object Parse(PacketParser p)
        {
            return new VitalsPercentagePacket()
            {
                LoginId = p.GetInt32(),
                HPPercentage = p.GetInt32() / 100.0f,
                MPPercentage = p.GetInt32() / 100.0f
            };
        }
    }
}
