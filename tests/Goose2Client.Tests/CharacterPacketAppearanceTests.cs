using Goose2Client;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class CharacterPacketAppearanceTests
{
    [Fact]
    public void LayeredMkc_Body10001_ParsesFullAppearance_AndTrailingFieldsAlign()
    {
        // Weapon (slot 5) stays nonzero so the parser's BodyState correction does not fire.
        var raw = "MKC42,0,Asp,T1,S1,G1,10,20,2,75,10001,10,20,30,40,4,10070,"
            + "11,100,90,80,255,12,90,80,70,255,13,80,70,60,255,14,70,60,50,255,"
            + "15,60,50,40,255,16,50,40,30,255,111,222,33,44,1,10002,123,1,10040,5,6,7,8";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));

        Assert.Equal(42, p.LoginId);
        Assert.Equal((CharacterType)0, p.CharacterType);
        Assert.Equal("Asp", p.Name);
        Assert.Equal("T1", p.Title);
        Assert.Equal("S1", p.Surname);
        Assert.Equal("G1", p.GuildName);
        Assert.Equal(9, p.MapX);
        Assert.Equal(19, p.MapY);
        Assert.Equal(Direction.Right, p.Facing);
        Assert.Equal(0.75f, p.HPPercent);
        Assert.Equal(10001, p.BodyId);
        Assert.Equal(10, p.BodyR);
        Assert.Equal(20, p.BodyG);
        Assert.Equal(30, p.BodyB);
        Assert.Equal(40, p.BodyA);
        Assert.Equal(4, p.BodyState);
        Assert.Equal(10070, p.HairId);
        Assert.Equal(new[] { 11, 100, 90, 80, 255 }, p.DisplayedEquipment[0]);
        Assert.Equal(new[] { 12, 90, 80, 70, 255 }, p.DisplayedEquipment[1]);
        Assert.Equal(new[] { 13, 80, 70, 60, 255 }, p.DisplayedEquipment[2]);
        Assert.Equal(new[] { 14, 70, 60, 50, 255 }, p.DisplayedEquipment[3]);
        Assert.Equal(new[] { 15, 60, 50, 40, 255 }, p.DisplayedEquipment[4]);
        Assert.Equal(new[] { 16, 50, 40, 30, 255 }, p.DisplayedEquipment[5]);
        Assert.Equal(111, p.HairR);
        Assert.Equal(222, p.HairG);
        Assert.Equal(33, p.HairB);
        Assert.Equal(44, p.HairA);
        Assert.Equal(1, p.Invisible);
        Assert.Equal(10002, p.FaceId);
        Assert.Equal(123, p.MoveSpeed);
        Assert.True(p.IsGM);
        Assert.Equal(new[] { 10040, 5, 6, 7, 8 }, p.DisplayedEquipment[6]);
    }

    [Fact]
    public void LayeredChp_Body10001_ParsesFullAppearance_AndTrailingFieldsAlign()
    {
        var raw = "CHP42,10001,10,20,30,40,5,10070,"
            + "11,100,90,80,255,12,90,80,70,255,13,80,70,60,255,14,70,60,50,255,"
            + "15,60,50,40,255,16,50,40,30,255,111,222,33,44,1,10002,123,10040,5,6,7,8";
        var p = (UpdateCharacterPacket)new UpdateCharacterPacket().Parse(new PacketParser(raw, "CHP"));

        Assert.Equal(42, p.LoginId);
        Assert.Equal(10001, p.BodyId);
        Assert.Equal(10, p.BodyR);
        Assert.Equal(20, p.BodyG);
        Assert.Equal(30, p.BodyB);
        Assert.Equal(40, p.BodyA);
        Assert.Equal(5, p.BodyState);
        Assert.Equal(10070, p.HairId);
        Assert.Equal(new[] { 11, 100, 90, 80, 255 }, p.DisplayedEquipment[0]);
        Assert.Equal(new[] { 12, 90, 80, 70, 255 }, p.DisplayedEquipment[1]);
        Assert.Equal(new[] { 13, 80, 70, 60, 255 }, p.DisplayedEquipment[2]);
        Assert.Equal(new[] { 14, 70, 60, 50, 255 }, p.DisplayedEquipment[3]);
        Assert.Equal(new[] { 15, 60, 50, 40, 255 }, p.DisplayedEquipment[4]);
        Assert.Equal(new[] { 16, 50, 40, 30, 255 }, p.DisplayedEquipment[5]);
        Assert.Equal(111, p.HairR);
        Assert.Equal(222, p.HairG);
        Assert.Equal(33, p.HairB);
        Assert.Equal(44, p.HairA);
        Assert.Equal(1, p.Invisible);
        Assert.Equal(10002, p.FaceId);
        Assert.Equal(123, p.MoveSpeed);
        Assert.Equal(new[] { 10040, 5, 6, 7, 8 }, p.DisplayedEquipment[6]);
    }

    [Fact]
    public void CompactMkc_Body10100_StaysCompact_AndTrailingFieldsAlign()
    {
        var raw = "MKC5,1,Compact,,,0,3,4,1,50,10100,255,0,0,255,0,1,777,0";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));

        Assert.Equal(5, p.LoginId);
        Assert.Equal("Compact", p.Name);
        Assert.Equal(2, p.MapX);
        Assert.Equal(3, p.MapY);
        Assert.Equal(10100, p.BodyId);
        Assert.Equal(0, p.BodyState);
        Assert.Equal(0, p.HairId);
        Assert.Null(p.DisplayedEquipment);
        Assert.Equal(0, p.HairR);
        Assert.Equal(0, p.HairG);
        Assert.Equal(0, p.HairB);
        Assert.Equal(0, p.HairA);
        Assert.Equal(1, p.Invisible);
        Assert.Equal(0, p.FaceId);
        Assert.Equal(777, p.MoveSpeed);
        Assert.False(p.IsGM);
    }

    [Fact]
    public void CompactChp_Body10100_StaysCompact_AndTrailingFieldsAlign()
    {
        var raw = "CHP9,10100,255,0,0,255,0,1,666";
        var p = (UpdateCharacterPacket)new UpdateCharacterPacket().Parse(new PacketParser(raw, "CHP"));

        Assert.Equal(9, p.LoginId);
        Assert.Equal(10100, p.BodyId);
        Assert.Equal(0, p.BodyState);
        Assert.Equal(0, p.HairId);
        Assert.Null(p.DisplayedEquipment);
        Assert.Equal(0, p.HairR);
        Assert.Equal(1, p.Invisible);
        Assert.Equal(0, p.FaceId);
        Assert.Equal(666, p.MoveSpeed);
    }

    [Fact]
    public void CompactMkc_Body150_Alignment_Unchanged()
    {
        var raw = "MKC6,1,Morp,,,0,3,4,1,50,150,255,0,0,255,0,1,555,1";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));

        Assert.Equal(150, p.BodyId);
        Assert.Null(p.DisplayedEquipment);
        Assert.Equal(1, p.Invisible);
        Assert.Equal(555, p.MoveSpeed);
        Assert.True(p.IsGM);
    }

    [Fact]
    public void CompactChp_Body150_Alignment_Unchanged()
    {
        var raw = "CHP10,150,255,0,0,255,0,1,444";
        var p = (UpdateCharacterPacket)new UpdateCharacterPacket().Parse(new PacketParser(raw, "CHP"));

        Assert.Equal(150, p.BodyId);
        Assert.Null(p.DisplayedEquipment);
        Assert.Equal(1, p.Invisible);
        Assert.Equal(444, p.MoveSpeed);
    }
}
