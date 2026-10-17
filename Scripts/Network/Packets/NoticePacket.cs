namespace Goose2Client.Network.Packets
{
    public class NoticePacket : PacketHandler
    {
        public NoticeType Type { get; set; }
        public string Text { get; set; } = "";

        public override string Prefix { get; } = "NTF";

        public override object Parse(PacketParser p)
        {
            return new NoticePacket()
            {
                Type = (NoticeType)p.GetInt32(),
                Text = p.LengthRemaining() > 0 ? p.GetRemaining() : ""
            };
        }
    }
}
