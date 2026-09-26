using Goose2Client.Network;

namespace Goose2Client.Network.Packets
{
    public class LogDefaultsMetadataPacket : PacketHandler
    {
        public override string Prefix { get; } = "LMD";

        public override object Parse(PacketParser p)
        {
            return LogPacketParsing.ParseLmd(p.GetWholePacket());
        }
    }
}
