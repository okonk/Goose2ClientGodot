using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class ServerMessagePacket : PacketHandler
    {
        public ChatType ChatType { get; set; }

        public string Message { get; set; }

        public override string Prefix { get; } = "$";

        public override object Parse(PacketParser p)
        {
            return new ServerMessagePacket()
            {
                ChatType = (ChatType)Convert.ToInt32(p.GetSubstring(1)),
                Message = p.GetRemaining()
            };
        }
    }
}
