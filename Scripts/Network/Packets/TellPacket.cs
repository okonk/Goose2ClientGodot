using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class TellPacket : PacketHandler
    {
        public string Name { get; set; }

        public bool IsAfk { get; set; }

        public string Message { get; set; }

        public override string Prefix { get; } = "&";

        public override object Parse(PacketParser p)
        {
            return new TellPacket()
            {
                Name = p.GetString(),
                IsAfk = p.GetInt32() != 0,
                Message = p.GetRemaining()
            };
        }
    }
}
