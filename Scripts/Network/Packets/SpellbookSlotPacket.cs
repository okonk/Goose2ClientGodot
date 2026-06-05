using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class SpellbookSlotPacket : PacketHandler
    {
        public int SlotNumber { get; set; }

        public string Name { get; set; }

        public int AnimationId { get; set; }

        public int Unknown1 { get; set; }

        public int SpellIndex { get; set; } // not sure what this means

        public SpellTargetType TargetType { get; set; }

        public int GraphicId { get; set; }

        public int GraphicFile { get; set; }

        public long Cooldown { get; set; }

        public override string Prefix { get; } = "SSS";

        public override object Parse(PacketParser p)
        {
            var packet = new SpellbookSlotPacket()
            {
                SlotNumber = p.GetInt32() - 1
            };

            if (p.LengthRemaining() > 0)
            {
                packet.Name = p.GetString();
                packet.AnimationId = p.GetInt32();
                packet.Unknown1 = p.GetInt32();
                packet.SpellIndex = p.GetInt32();
                packet.TargetType = (SpellTargetType)p.GetInt32();
                packet.GraphicId = p.GetInt32();
                packet.GraphicFile = p.GetInt32();
                packet.Cooldown = p.GetInt64();
            }

            return packet;
        }
    }
}
