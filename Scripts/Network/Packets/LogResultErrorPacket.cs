using Goose2Client.Network;

namespace Goose2Client.Network.Packets
{
    public class LogResultErrorPacket : PacketHandler
    {
        public override string Prefix { get; } = "LRX";

        public override object Parse(PacketParser p)
        {
            return LogPacketParsing.ParseLrx(p.GetWholePacket());
        }
    }
}
