using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class AdminModeActivatePacket : PacketHandler
    {
        public int LoginId { get; set; }

        public int Enabled { get; set; }

        public override string Prefix { get; } = "AMA";

        public override object Parse(PacketParser p)
        {
            return new AdminModeActivatePacket()
            {
                LoginId = p.GetInt32(),
                Enabled = p.GetInt32()
            };
        }
    }
}
