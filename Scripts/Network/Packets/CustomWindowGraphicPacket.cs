namespace Goose2Client.Network.Packets
{
    public class CustomWindowGraphicPacket : PacketHandler
    {
        public int EquippedId { get; set; }
        public int Pose { get; set; }

        public override string Prefix { get; } = "CWG";

        public override object Parse(PacketParser p)
        {
            return new CustomWindowGraphicPacket()
            {
                EquippedId = p.GetInt32(),
                Pose = p.GetInt32(),
            };
        }

        public static string FormatCws(int lookSlot, int statsSlot)
        {
            return $"CWS{lookSlot},{statsSlot}";
        }

        public static string FormatCwc(int lookSlot, int statsSlot, int r, int g, int b, int a, string name)
        {
            return $"CWC{lookSlot},{statsSlot},{r},{g},{b},{a},{name}";
        }
    }
}
