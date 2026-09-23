using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class PartyBuffAddPacket : PacketHandler
    {
        public int LoginId { get; set; }
        public int EffectId { get; set; }
        public int GraphicId { get; set; }
        public int GraphicFile { get; set; }
        public long RemainingMs { get; set; }
        public long TotalMs { get; set; }
        public string Name { get; set; }

        public override string Prefix { get; } = "PBA";

        public override object Parse(PacketParser p)
        {
            // Name is the final field and may contain commas, so it is the un-tokenized suffix.
            return new PartyBuffAddPacket()
            {
                LoginId = p.GetInt32(),
                EffectId = p.GetInt32(),
                GraphicId = p.GetInt32(),
                GraphicFile = p.GetInt32(),
                RemainingMs = p.GetInt64(),
                TotalMs = p.GetInt64(),
                Name = p.GetRemaining()
            };
        }
    }
}
