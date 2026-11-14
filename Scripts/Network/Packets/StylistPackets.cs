namespace Goose2Client.Network.Packets
{
    public static class StylistPackets
    {
        public static string FormatStc(int hair, int face) => $"STC{hair},{face}";
    }
}
