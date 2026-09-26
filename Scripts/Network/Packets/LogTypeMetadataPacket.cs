using Goose2Client.Network;

namespace Goose2Client.Network.Packets
{
    public class LogTypeMetadataPacket : PacketHandler
    {
        public override string Prefix { get; } = "LMT";

        public override object Parse(PacketParser p)
        {
            return LogPacketParsing.ParseLmt(p.GetWholePacket());
        }
    }
}
