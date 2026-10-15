using System.Globalization;

namespace Goose2Client.Network.Packets
{
    public class AmountInfoPacket : PacketHandler
    {
        public int WindowId { get; set; }
        public int Sheet { get; set; }
        public int Graphic { get; set; }
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
        public int A { get; set; }
        public long Min { get; set; }
        public long Max { get; set; }
        public long Default { get; set; }
        public string Prompt { get; set; } = "";

        public override string Prefix { get; } = "AMI";

        public override object Parse(PacketParser p)
        {
            p.Delimeter = '|';
            return new AmountInfoPacket()
            {
                WindowId = p.GetInt32(),
                Sheet = p.GetInt32(),
                Graphic = p.GetInt32(),
                R = p.GetInt32(),
                G = p.GetInt32(),
                B = p.GetInt32(),
                A = p.GetInt32(),
                Min = p.GetInt64(),
                Max = p.GetInt64(),
                Default = p.GetInt64(),
                // GetRemaining throws when the packet ends right after the default
                Prompt = p.LengthRemaining() > 0 ? p.GetRemaining() : ""
            };
        }

        public static string FormatAmt(int windowId, long amount)
        {
            return $"AMT{windowId.ToString(CultureInfo.InvariantCulture)},{amount.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
