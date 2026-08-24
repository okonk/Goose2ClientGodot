namespace Goose2Client.Network.Packets
{
    public class MapFlagsPacket : PacketHandler
    {
        public bool PvPEnabled { get; set; }
        public bool ItemsEnabled { get; set; }
        public bool SpellsEnabled { get; set; }

        public override string Prefix { get; } = "MFL";

        public override object Parse(PacketParser p)
        {
            var packet = new MapFlagsPacket()
            {
                PvPEnabled = p.GetBool(),
                ItemsEnabled = p.GetBool(),
                SpellsEnabled = p.GetBool()
            };

            return packet;
        }
    }
}
