using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class WindowOpeningLinePacketTests
    {
        [Fact]
        public void ParsesWindowIdAndMessage()
        {
            var p = (WindowOpeningLinePacket)new WindowOpeningLinePacket().Parse(
                new PacketParser("WNL1002,Welcome to my shop", "WNL"));
            Assert.Equal(1002, p.WindowId);
            Assert.Equal("Welcome to my shop", p.Text);
        }

        [Fact]
        public void MessageMayContainCommasAndPipes()
        {
            var p = (WindowOpeningLinePacket)new WindowOpeningLinePacket().Parse(
                new PacketParser("WNL1002,Hello, world|stuff", "WNL"));
            Assert.Equal(1002, p.WindowId);
            Assert.Equal("Hello, world|stuff", p.Text);
        }

        [Fact]
        public void EmptyMessageParsesEmpty()
        {
            var p = (WindowOpeningLinePacket)new WindowOpeningLinePacket().Parse(
                new PacketParser("WNL1002,", "WNL"));
            Assert.Equal(1002, p.WindowId);
            Assert.Equal("", p.Text);
        }
    }

    public class WindowLinePacketTests
    {
        [Fact]
        public void ParsesSheetGraphicAndTint()
        {
            var p = (WindowLinePacket)new WindowLinePacket().Parse(
                new PacketParser("WNF1001,3,Do it|0|0|5|7|255|128|0|255", "WNF"));
            Assert.Equal(1001, p.WindowId);
            Assert.Equal(2, p.LineNumber);
            Assert.Equal("Do it", p.Text);
            Assert.Equal(5, p.GraphicSheet);
            Assert.Equal(7, p.GraphicId);
            Assert.Equal(255, p.GraphicR);
            Assert.Equal(128, p.GraphicG);
            Assert.Equal(0, p.GraphicB);
            Assert.Equal(255, p.GraphicA);
        }

        [Fact]
        public void StarShortcutMeansNoTint()
        {
            var p = (WindowLinePacket)new WindowLinePacket().Parse(
                new PacketParser("WNF1001,1,Hi|0|0|0|0|*", "WNF"));
            Assert.Equal(0, p.LineNumber);
            Assert.Equal("Hi", p.Text);
            Assert.Equal(0, p.GraphicSheet);
            Assert.Equal(0, p.GraphicId);
            Assert.Equal(0, p.GraphicR);
            Assert.Equal(0, p.GraphicA);
        }
    }
}
