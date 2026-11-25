namespace Goose2Client.Network.Packets
{
    public class ZoneDeletePacket : PacketHandler
    {
        public int Id { get; set; }

        public override string Prefix { get; } = "ZDEL";

        public override object Parse(PacketParser p) => new ZoneDeletePacket { Id = p.GetInt32() };
    }
}
