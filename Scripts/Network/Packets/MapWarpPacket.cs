namespace Goose2Client.Network.Packets
{
    // Wire format: MWP<x>,<y> — 1-based; position only, later fields may be appended (server: P.MapWarp).
    public class MapWarpPacket : PacketHandler
    {
        public int X { get; set; }
        public int Y { get; set; }

        public override string Prefix { get; } = "MWP";

        public override object Parse(PacketParser p)
        {
            var packet = new MapWarpPacket()
            {
                X = p.GetInt32() - 1,
                Y = p.GetInt32() - 1
            };

            return packet;
        }
    }
}
