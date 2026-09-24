using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class PartyBuffPacketTests
    {
        [Fact]
        public void Pba_ParsesAllFieldsWith64BitDurations()
        {
            var p = (PartyBuffAddPacket)new PartyBuffAddPacket().Parse(
                new PacketParser("PBA17,203,5,12,9007199254740993,18014398509481985,Regen", "PBA"));

            Assert.Equal(17, p.LoginId);
            Assert.Equal(203, p.EffectId);
            Assert.Equal(5, p.GraphicId);
            Assert.Equal(12, p.GraphicFile);
            Assert.Equal(9007199254740993L, p.RemainingMs);
            Assert.Equal(18014398509481985L, p.TotalMs);
            Assert.Equal("Regen", p.Name);
        }

        [Fact]
        public void Pba_ZeroDurationPermanentEffect_ParsesZeroDurations()
        {
            var p = (PartyBuffAddPacket)new PartyBuffAddPacket().Parse(
                new PacketParser("PBA3,9,1,2,0,0,Stasis", "PBA"));

            Assert.Equal(3, p.LoginId);
            Assert.Equal(9, p.EffectId);
            Assert.Equal(0, p.RemainingMs);
            Assert.Equal(0, p.TotalMs);
            Assert.Equal("Stasis", p.Name);
        }

        [Fact]
        public void Pba_NameContainingCommas_ParsedAsUnTokenizedRemaining()
        {
            var p = (PartyBuffAddPacket)new PartyBuffAddPacket().Parse(
                new PacketParser("PBA4,11,7,8,1000,2000,Strength, Greater", "PBA"));

            Assert.Equal(1000, p.RemainingMs);
            Assert.Equal(2000, p.TotalMs);
            Assert.Equal("Strength, Greater", p.Name);
        }

        [Fact]
        public void Pbr_ParsesExactLoginAndEffectIds()
        {
            var p = (PartyBuffRemovePacket)new PartyBuffRemovePacket().Parse(
                new PacketParser("PBR25,314", "PBR"));

            Assert.Equal(25, p.LoginId);
            Assert.Equal(314, p.EffectId);
        }

        [Fact]
        public void Pbc_ParsesExactLoginId()
        {
            var p = (PartyBuffClearPacket)new PartyBuffClearPacket().Parse(
                new PacketParser("PBC63", "PBC"));

            Assert.Equal(63, p.LoginId);
        }
    }
}
