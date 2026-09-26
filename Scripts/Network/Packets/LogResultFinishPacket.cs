using Goose2Client.Network;

namespace Goose2Client.Network.Packets
{
    public class LogResultFinishPacket : PacketHandler
    {
        public override string Prefix { get; } = "LRF";

        public override object Parse(PacketParser p)
        {
            return LogPacketParsing.ParseLrf(p.GetWholePacket());
        }
    }
}
