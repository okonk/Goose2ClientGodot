using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class WindowLinePacket : PacketHandler
    {
        public int WindowId { get; set; }

        public int LineNumber { get; set; }

        public string Text { get; set; }

        public int StackSize { get; set; }

        public int ItemId { get; set; }

        public int GraphicSheet { get; set; }
        public int GraphicId { get; set; }

        public int GraphicR { get; set; }
        public int GraphicG { get; set; }
        public int GraphicB { get; set; }
        public int GraphicA { get; set; }

        public override string Prefix { get; } = "WNF";

        public override object Parse(PacketParser p)
        {
            // WNF1023,1,Experience Sold: 0|0|0|0|0|*
            p.Delimeter = ',';
            var packet = new WindowLinePacket()
            {
                WindowId = p.GetInt32(),
                LineNumber = p.GetInt32() - 1
            };

            p.Delimeter = '|';

            packet.Text = p.GetString();
            packet.StackSize = p.GetInt32();
            packet.ItemId = p.GetInt32();
            packet.GraphicSheet = p.GetInt32();
            packet.GraphicId = p.GetInt32();

            if (p.Peek() == '*')
            {
                p.GetString();
                packet.GraphicR = 0;
                packet.GraphicG = 0;
                packet.GraphicB = 0;
                packet.GraphicA = 0;
            }
            else
            {
                packet.GraphicR = p.GetInt32();
                packet.GraphicG = p.GetInt32();
                packet.GraphicB = p.GetInt32();
                packet.GraphicA = p.GetInt32();
            }

            return packet;
        }
    }
}
