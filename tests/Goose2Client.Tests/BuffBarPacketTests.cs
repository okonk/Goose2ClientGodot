using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class BuffBarPacketTests
    {
        [Fact]
        public void ParsesRemainingMs()
        {
            var p = (BuffBarPacket)new BuffBarPacket().Parse(new PacketParser("BUF1,5,12,Speed,120000", "BUF"));
            Assert.Equal(0, p.SlotNumber);
            Assert.Equal(5, p.GraphicId);
            Assert.Equal(12, p.GraphicFile);
            Assert.Equal("Speed", p.Name);
            Assert.Equal(120000, p.RemainingMs);
        }

        [Fact]
        public void OldServerPacketWithoutDuration_ParsesZero()
        {
            var p = (BuffBarPacket)new BuffBarPacket().Parse(new PacketParser("BUF1,5,12,Speed", "BUF"));
            Assert.Equal("Speed", p.Name);
            Assert.Equal(0, p.RemainingMs);
        }

        [Fact]
        public void EmptySlot_ParsesDefaults()
        {
            var p = (BuffBarPacket)new BuffBarPacket().Parse(new PacketParser("BUF3", "BUF"));
            Assert.Equal(2, p.SlotNumber);
            Assert.Equal(0, p.RemainingMs);
            Assert.Null(p.Name);
        }
    }
}
