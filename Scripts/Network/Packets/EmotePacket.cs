using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class EmotePacket : PacketHandler
    {
        public int LoginId { get; set; }

        public int AnimationId { get; set; }

        public int GraphicFile { get; set; }

        public override string Prefix { get; } = "EMOT";

        public override object Parse(PacketParser p)
        {
            return new EmotePacket()
            {
                LoginId = p.GetInt32(),
                AnimationId = p.GetInt32(),
                GraphicFile = p.GetInt32(),
            };
        }
    }
}
