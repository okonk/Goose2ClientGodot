using Goose2Client.Network;

namespace Goose2Client.Network.Packets
{
    public class LogResultBeginPacket : PacketHandler
    {
        public override string Prefix { get; } = "LRB";

        public override object Parse(PacketParser p)
        {
            return LogPacketParsing.ParseLrb(p.GetWholePacket());
        }
    }
}
