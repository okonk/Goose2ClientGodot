using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class CharacterPacketInvisibleTests
{
    [Fact]
    public void MonsterMkc_InvisibleToken_IsStoredAndTrailingFieldsAlign()
    {
        var raw = "MKC7,1,Mon,,,0,3,4,1,50,150,255,0,0,255,0,1,999,1";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.Equal(1, p.Invisible);
        Assert.Equal(999, p.MoveSpeed);
        Assert.True(p.IsGM);
    }

    [Fact]
    public void MonsterChp_InvisibleToken_IsStoredAndTrailingFieldsAlign()
    {
        var raw = "CHP8,150,255,0,0,255,0,1,888";
        var p = (UpdateCharacterPacket)new UpdateCharacterPacket().Parse(new PacketParser(raw, "CHP"));
        Assert.Equal(1, p.Invisible);
        Assert.Equal(888, p.MoveSpeed);
    }
}
