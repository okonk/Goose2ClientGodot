using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    // WNL<windowId>,<message> — sets the non-clickable opening line of a window.
    // The message is the rest of the packet after the first comma, so it may itself
    // contain commas/pipes. Sent after MKW and before ENW (the window already exists).
    class WindowOpeningLinePacket : PacketHandler
    {
        public int WindowId { get; set; }

        public string Text { get; set; }

        public override string Prefix { get; } = "WNL";

        public override object Parse(PacketParser p)
        {
            p.Delimeter = ',';
            var packet = new WindowOpeningLinePacket
            {
                WindowId = p.GetInt32()
            };
            packet.Text = p.LengthRemaining() > 0 ? p.GetRemaining() : "";
            return packet;
        }
    }
}
