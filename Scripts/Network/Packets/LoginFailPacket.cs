using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class LoginFailPacket : PacketHandler
    {
        public string Message { get; set; }

        public override string Prefix { get; } = "LNO";

        public override object Parse(PacketParser p)
        {
            return new LoginFailPacket() { Message = p.GetRemaining() };
        }
    }
}
