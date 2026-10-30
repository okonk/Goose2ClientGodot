using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    public class MakeWindowPacket : PacketHandler
    {
        public int WindowId { get; set; }
        public WindowFrames WindowFrame { get; set; }
        public string Title { get; set; } = null!;
        public bool[] Buttons { get; set; } = Array.Empty<bool>();
        public int NpcId { get; set; }
        // For GridContainer, FrameParam1 is slot count and FrameParam2 is columns.
        public int FrameParam1 { get; set; }
        public int FrameParam2 { get; set; }

        public override string Prefix { get; } = "MKW";

        public override object Parse(PacketParser p)
        {
            // MKW1001,13,Welcome to my shop!,0,1,0,0,0,1201,0,0
            return new MakeWindowPacket()
            {
                WindowId = p.GetInt32(),
                WindowFrame = (WindowFrames)p.GetInt32(),
                Title = p.GetString(),
                Buttons = new bool[5] { p.GetBool(), p.GetBool(), p.GetBool(), p.GetBool(), p.GetBool() },
                NpcId = p.GetInt32(),
                FrameParam1 = p.GetInt32(),
                FrameParam2 = p.GetInt32(),
            };
        }
    }
}
