using System;

namespace Goose2Client.Network.Packets
{
    public class EnchantPreviewPacket : PacketHandler
    {
        public int WindowId { get; set; }
        public int ItemSlot { get; set; }
        public int EssenceSlot { get; set; }
        public int ProtectionSlot { get; set; }
        public bool CanEnchant { get; set; }
        public string[] Lines { get; set; } = Array.Empty<string>();

        public override string Prefix { get; } = "EWP";

        public override object Parse(PacketParser p)
        {
            var parts = p.GetRemaining().Split('|');
            var h = parts[0].Split(',');
            return new EnchantPreviewPacket()
            {
                WindowId = int.Parse(h[0]),
                ItemSlot = int.Parse(h[1]),
                EssenceSlot = int.Parse(h[2]),
                ProtectionSlot = int.Parse(h[3]),
                CanEnchant = h[4] != "0",
                Lines = parts[1..],
            };
        }

        public static string FormatEws(int itemSlot, int essenceSlot, int protectionSlot)
        {
            return $"EWS{itemSlot},{essenceSlot},{protectionSlot}";
        }

        public static string FormatEwc(int itemSlot, int essenceSlot, int protectionSlot)
        {
            return $"EWC{itemSlot},{essenceSlot},{protectionSlot}";
        }
    }
}
