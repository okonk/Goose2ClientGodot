using Goose2Client;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class MakeWindowPacketTests
    {
        [Fact]
        public void Parse_GridContainer_ReadsSlotsAndColumns()
        {
            var p = (MakeWindowPacket)new MakeWindowPacket().Parse(new PacketParser("MKW1042,32,Backpack,0,1,0,0,0,0,20,5", "MKW"));

            Assert.Equal(1042, p.WindowId);
            Assert.Equal(WindowFrames.GridContainer, p.WindowFrame);
            Assert.Equal("Backpack", p.Title);
            Assert.True(p.Buttons[1]);
            Assert.Equal(0, p.NpcId);
            Assert.Equal(20, p.FrameParam1);
            Assert.Equal(5, p.FrameParam2);
        }

        [Fact]
        public void Parse_Vendor_Regression()
        {
            var p = (MakeWindowPacket)new MakeWindowPacket().Parse(new PacketParser("MKW1001,13,Welcome to my shop!,0,1,0,0,0,1201,0,0", "MKW"));

            Assert.Equal(WindowFrames.Vendor, p.WindowFrame);
            Assert.Equal(1201, p.NpcId);
            Assert.Equal(0, p.FrameParam1);
            Assert.Equal(0, p.FrameParam2);
        }
    }
}
