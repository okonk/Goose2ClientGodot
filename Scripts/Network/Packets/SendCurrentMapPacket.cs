using System;
using System.Collections.Generic;

namespace Goose2Client.Network.Packets
{
    class SendCurrentMapPacket : PacketHandler
    {
        public string MapFileName { get; set; }

        public int MapVersion { get; set; }

        public string MapName { get; set; }

        public override string Prefix { get; } = "SCM";

        public override object Parse(PacketParser p)
        {
            // SCMMapId,MapVersion,MapName
            return new SendCurrentMapPacket()
            {
                MapFileName = p.GetString(),
                MapVersion = p.GetInt32(),
                MapName = p.GetString(),
                // someOtherVersion = p.GetInt32()
            };
        }
    }
}
