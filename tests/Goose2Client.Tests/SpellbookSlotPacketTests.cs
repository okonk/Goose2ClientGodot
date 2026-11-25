using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class SpellbookSlotPacketTests
    {
        [Fact]
        public void ParsesCooldownRemainingMs()
        {
            var p = (SpellbookSlotPacket)new SpellbookSlotPacket().Parse(new PacketParser("SSS1,Fireball,5,1000,0,1,100,3,20000,4500", "SSS"));
            Assert.Equal(0, p.SlotNumber);
            Assert.Equal("Fireball", p.Name);
            Assert.Equal(20000, p.Cooldown);
            Assert.Equal(4500, p.CooldownRemainingMs);
        }

        [Fact]
        public void OldServerPacketWithoutRemaining_ParsesZero()
        {
            var p = (SpellbookSlotPacket)new SpellbookSlotPacket().Parse(new PacketParser("SSS1,Fireball,5,1000,0,1,100,3,20000", "SSS"));
            Assert.Equal(20000, p.Cooldown);
            Assert.Equal(0, p.CooldownRemainingMs);
        }

        [Fact]
        public void EmptySlot_ParsesZeroRemaining()
        {
            var p = (SpellbookSlotPacket)new SpellbookSlotPacket().Parse(new PacketParser("SSS5,,0,0,0,0,0,0,0", "SSS"));
            Assert.Equal(4, p.SlotNumber);
            Assert.Equal(0, p.Cooldown);
            Assert.Equal(0, p.CooldownRemainingMs);
        }

        [Fact]
        public void ParsesGroundTargetingShapeAndSize()
        {
            var p = (SpellbookSlotPacket)new SpellbookSlotPacket().Parse(new PacketParser("SSS1,Fire Patch,5,1000,0,1,100,3,20000,0,1,5,2", "SSS"));
            Assert.True(p.GroundTargetable);
            Assert.Equal(SpellShape.Area, p.Shape);
            Assert.Equal(2, p.ShapeSize);
        }

        [Fact]
        public void OlderServerWithoutShapeFields_DefaultsToTarget()
        {
            var p = (SpellbookSlotPacket)new SpellbookSlotPacket().Parse(new PacketParser("SSS1,Fireball,5,1000,0,1,100,3,20000,4500", "SSS"));
            Assert.False(p.GroundTargetable);
            Assert.Equal(SpellShape.Target, p.Shape);
            Assert.Equal(0, p.ShapeSize);
        }
    }
}
