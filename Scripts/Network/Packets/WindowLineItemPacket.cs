namespace Goose2Client.Network.Packets
{
    public class WindowLineItemPacket : InventorySlotPacket
    {
        public int WindowId { get; set; }

        public int LineNumber { get; set; }

        public override string Prefix { get; } = "WLI";

        public override object Parse(PacketParser p)
        {
            // WLIwindowid,linesequence|<item slot payload, as in SVS>
            var pkt = new WindowLineItemPacket();
            p.Delimeter = ',';
            pkt.WindowId = p.GetInt32();
            p.Delimeter = '|';
            pkt.LineNumber = p.GetInt32() - 1;
            ReadFieldsInto(p, pkt);
            pkt.SlotNumber = pkt.LineNumber;
            return pkt;
        }
    }
}
