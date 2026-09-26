using Goose2Client.Network;

namespace Goose2Client.Network.Packets
{
    public class LogResultDataPacket : PacketHandler
    {
        public override string Prefix { get; } = "LRD";

        public override object Parse(PacketParser p)
        {
            return LogPacketParsing.ParseLrd(p.GetWholePacket());
        }
    }
}
