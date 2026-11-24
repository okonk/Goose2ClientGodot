using System.Globalization;

namespace Goose2Client.Network.Packets
{
    public class TextInputInfoPacket : PacketHandler
    {
        public int WindowId { get; set; }
        public int MaxLength { get; set; }
        public string ButtonText { get; set; } = null!;
        public string DefaultText { get; set; } = null!;
        public string Prompt { get; set; } = "";

        public override string Prefix { get; } = "TXI";

        public override object Parse(PacketParser p)
        {
            p.Delimeter = '|';
            return new TextInputInfoPacket()
            {
                WindowId = p.GetInt32(),
                MaxLength = p.GetInt32(),
                ButtonText = p.GetString(),
                DefaultText = p.GetString(),
                // GetRemaining throws when the packet ends right after the default text
                Prompt = p.LengthRemaining() > 0 ? p.GetRemaining() : ""
            };
        }

        public static string FormatTxt(int windowId, string text)
        {
            return $"TXT{windowId.ToString(CultureInfo.InvariantCulture)},{text}";
        }
    }
}
