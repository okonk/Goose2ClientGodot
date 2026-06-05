using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class SendMapNamePacket : PacketHandler
    {
        public string MapName { get; set; }

        public override string Prefix { get; } = "SMN";

        public override object Parse(PacketParser p)
        {
            // SMNMapName
            return new SendMapNamePacket()
            {
                MapName = p.GetString()
            };
        }
    }
}
