using System.Linq;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class WindowLineItemPacketTests
    {
        [Fact]
        public void ParsesWindowIdLineNumberAndItemFields()
        {
            var fields = Enumerable.Repeat("0", 43).ToArray();
            fields[0] = "3";
            fields[4] = "Cloth";
            var body = string.Join("|", fields.Concat(["gold", "", "0"]));

            var p = (WindowLineItemPacket)new WindowLineItemPacket().Parse(
                new PacketParser($"WLI1001,2|{body}", "WLI"));

            Assert.Equal(1001, p.WindowId);
            Assert.Equal(1, p.LineNumber);
            Assert.Equal(1, p.SlotNumber);
            Assert.Equal("Cloth", p.Name);
            Assert.Equal("gold", p.CurrencyName);
        }

        [Fact]
        public void ItemNameContainingCommasDoesNotShiftTheHeader()
        {
            var fields = Enumerable.Repeat("0", 43).ToArray();
            fields[4] = "Sword, Long";
            var body = string.Join("|", fields);

            var p = (WindowLineItemPacket)new WindowLineItemPacket().Parse(
                new PacketParser($"WLI1001,1|{body}", "WLI"));

            Assert.Equal(1001, p.WindowId);
            Assert.Equal(0, p.LineNumber);
            Assert.Equal("Sword, Long", p.Name);
        }
    }
}
