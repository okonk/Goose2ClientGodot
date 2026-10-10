using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class CharacterPacketHideNameTests
{
    // Layered player MKC (appearance-test sample) with HideName=1 inserted right after Invisible
    // (the token between the hair-rgba "44,1" and FaceId 10002).
    [Fact]
    public void LayeredMkc_HideNameAfterInvisible_IsParsed()
    {
        var raw = "MKC42,1,Asp,T1,S1,G1,10,20,2,75,10001,10,20,30,40,4,10070,"
            + "11,100,90,80,255,12,90,80,70,255,13,80,70,60,255,14,70,60,50,255,"
            + "15,60,50,40,255,16,50,40,30,255,111,222,33,44,1,1,10002,123,1,10040,5,6,7,8";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.Equal(1, p.Invisible);
        Assert.True(p.HideName);
        Assert.Equal(10002, p.FaceId);      // guards correct token placement
        Assert.Equal(123, p.MoveSpeed);
    }

    // Monster (non-layered, body 150) MKC: Invisible=0, HideName=1, MoveSpeed=320, IsGM=0.
    [Fact]
    public void MonsterMkc_HideNameAfterInvisible_IsParsed()
    {
        var raw = "MKC7,2,Mon,,,,1,1,1,1,150,0,0,0,0,3,0,1,320,0";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.Equal(0, p.Invisible);
        Assert.True(p.HideName);
        Assert.Equal(320, p.MoveSpeed);
        Assert.False(p.IsGM);
    }

    // Pre-hide_name servers omit the token entirely, so the tail is move speed and is_gm only.
    [Fact]
    public void MonsterMkc_WithoutHideNameToken_DefaultsHideNameFalse()
    {
        var raw = "MKC83267,2,Mouse,,,,70,43,4,100,10113,0,0,0,0,3,0,320,0,";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.Equal(0, p.Invisible);
        Assert.False(p.HideName);
        Assert.Equal(320, p.MoveSpeed);
        Assert.False(p.IsGM);
    }

    [Fact]
    public void LayeredMkc_WithoutHideNameToken_DefaultsHideNameFalse()
    {
        var raw = "MKC7,1,Hax,T,S,,1,2,2,50,10001,10,20,30,40,4,11,"
            + "0,*,0,*,0,*,0,*,0,*,101,1,2,3,4,5,6,7,8,"
            + "1,70,0,1,202,5,6,7,8,";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.Equal(1, p.Invisible);
        Assert.False(p.HideName);
        Assert.Equal(70, p.FaceId);
        Assert.Equal(0, p.MoveSpeed);
        Assert.True(p.IsGM);
        Assert.Equal(new[] { 202, 5, 6, 7, 8 }, p.DisplayedEquipment[6]);
    }

    [Fact]
    public void LayeredMkc_WithoutHideNameTokenAndColorlessMount_DefaultsHideNameFalse()
    {
        var raw = "MKC7,1,Hax,T,S,,1,2,2,50,10001,10,20,30,40,4,11,"
            + "0,*,0,*,0,*,0,*,0,*,101,1,2,3,4,5,6,7,8,"
            + "1,70,0,0,0,*";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.Equal(1, p.Invisible);
        Assert.False(p.HideName);
        Assert.Equal(70, p.FaceId);
        Assert.Equal(0, p.MoveSpeed);
        Assert.False(p.IsGM);
        Assert.Equal(0, p.DisplayedEquipment[6][0]);
    }

    [Fact]
    public void MonsterMkc_WithoutTrailingIsGmToken_DefaultsIsGmFalse()
    {
        var raw = "MKC7,2,Mon,,,,1,1,1,1,150,0,0,0,0,3,0,320,";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.False(p.HideName);
        Assert.Equal(320, p.MoveSpeed);
        Assert.False(p.IsGM);
    }
}
