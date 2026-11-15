using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class CharacterPacketRespawnTests
{
    private const string LayeredHead = "MKC42,1,Asp,T1,S1,G1,10,20,2,75,1,10,20,30,40,4,10070,"
        + "0,*,0,*,0,*,0,*,0,*,0,*,111,222,33,44,0,0,70,320,0,";

    private static MakeCharacterPacket Mkc(string raw)
        => (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));

    private static UpdateCharacterPacket Chp(string raw)
        => (UpdateCharacterPacket)new UpdateCharacterPacket().Parse(new PacketParser(raw, "CHP"));

    [Theory]
    [InlineData("0,*,0", 0)]
    [InlineData("0,*,47", 47)]
    [InlineData("273,*,12", 12)]
    [InlineData("273,1,2,3,4,9", 9)]
    public void Layered_player_mkc_reads_respawn_after_mount(string tail, int expected)
    {
        var p = Mkc(LayeredHead + tail);
        Assert.Equal(expected, p.RespawnSeconds);
        Assert.Equal(70, p.FaceId);
        Assert.Equal(320, p.MoveSpeed);
    }

    [Fact]
    public void Layered_player_mkc_without_respawn_token_is_alive()
        => Assert.Equal(0, Mkc(LayeredHead + "0,*").RespawnSeconds);

    [Fact]
    public void Non_layered_player_mkc_reads_respawn()
    {
        var p = Mkc("MKC7,1,Morph,,,,1,1,1,0,150,0,0,0,0,3,0,0,320,0,33");
        Assert.Equal(33, p.RespawnSeconds);
        Assert.Equal(320, p.MoveSpeed);
        Assert.False(p.IsGM);
    }

    [Fact]
    public void Layered_npc_mkc_is_alive()
    {
        var p = Mkc("MKC20001,2,Guard,,,,1,1,1,100,1,0,0,0,0,3,10070,"
            + "0,*,0,*,0,*,0,*,0,*,0,*,0,0,0,0,0,1,70,320,0,0,0,0,0,0");
        Assert.Equal(0, p.RespawnSeconds);
        Assert.True(p.HideName);
    }

    [Theory]
    [InlineData("0,*,0", 0)]
    [InlineData("0,*,58", 58)]
    [InlineData("273,1,2,3,4,9", 9)]
    public void Layered_chp_reads_respawn_after_mount(string tail, int expected)
    {
        var p = Chp("CHP42,1,10,20,30,40,4,10070,0,*,0,*,0,*,0,*,0,*,0,*,111,222,33,44,0,70,320," + tail);
        Assert.Equal(expected, p.RespawnSeconds);
        Assert.Equal(320, p.MoveSpeed);
    }

    [Fact]
    public void Non_layered_chp_reads_respawn()
    {
        var p = Chp("CHP8,150,255,0,0,255,0,0,888,21");
        Assert.Equal(888, p.MoveSpeed);
        Assert.Equal(21, p.RespawnSeconds);
    }
}
