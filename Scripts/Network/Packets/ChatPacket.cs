using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class ChatPacket : PacketHandler
    {
        public int LoginId { get; set; }

        public string Message { get; set; }

        public override string Prefix { get; } = "^";

        public override object Parse(PacketParser p)
        {
            return new ChatPacket()
            {
                LoginId = p.GetInt32(),
                Message = p.GetRemaining()
            };
        }
    }
}
