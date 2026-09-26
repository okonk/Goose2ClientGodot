using Goose2Client.Network;

namespace Goose2Client.Network.Packets
{
    public class LogMapMetadataPacket : PacketHandler
    {
        public override string Prefix { get; } = "LMM";

        public override object Parse(PacketParser p)
        {
            return LogPacketParsing.ParseLmm(p.GetWholePacket());
        }
    }
}
