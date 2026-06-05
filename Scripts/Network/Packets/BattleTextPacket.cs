using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public enum BattleTextType
    {
        White = 0,
        Red1 = 1,
        Red2 = 2,
        Red4 = 4,
        Red5 = 5,
        Green7 = 7,
        Green8 = 8,
        Stunned10 = 10,
        Rooted11 = 11,
        Dodge20 = 20,
        Miss21 = 21,
        Stunned50 = 50,
        Rooted51 = 51,
        Yellow60 = 60,
        Red61 = 61
    }

    class BattleTextPacket : PacketHandler
    {
        public int LoginId { get; set; }
        public BattleTextType BattleTextType { get; set; }
        public string Text { get; set; }
        public string Name { get; set; }

        public override string Prefix { get; } = "BT";

        public override object Parse(PacketParser p)
        {
            var packet = new BattleTextPacket()
            {
                LoginId = p.GetInt32(),
                BattleTextType = (BattleTextType)p.GetInt32()
            };

            if (p.LengthRemaining() > 0)
            {
                packet.Text = p.GetString();
            }

            if (p.LengthRemaining() > 0)
            {
                packet.Name = p.GetString();
            }

            return packet;
        }
    }
}
