using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class ExperienceBarPacket : PacketHandler
    {
        public float Percentage { get; set; }
        public long Experience { get; set; }
        public long ExperienceToNextLevel { get; set; }
        public long ExperienceSold { get; set; }

        public override string Prefix { get; } = "TNL";

        public override object Parse(PacketParser p)
        {
            return new ExperienceBarPacket()
            {
                Percentage = p.GetInt32() / 100.0f,
                Experience = p.GetInt64(),
                ExperienceToNextLevel = p.GetInt64(),
                ExperienceSold = p.GetInt64(),
            };
        }
    }
}
