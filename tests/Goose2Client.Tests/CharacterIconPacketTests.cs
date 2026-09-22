using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class CharacterIconPacketTests
{
    [Fact]
    public void Chi_PositiveSheetAndGraphic_StoresAllFields()
    {
        var raw = "CHI42,2276,332038";
        var p = (CharacterIconPacket)new CharacterIconPacket().Parse(new PacketParser(raw, "CHI"));
        Assert.Equal(42, p.LoginId);
        Assert.Equal(2276, p.Sheet);
        Assert.Equal(332038, p.Graphic);
    }

    [Fact]
    public void Chi_ZeroZero_StoresAllFields()
    {
        var raw = "CHI42,0,0";
        var p = (CharacterIconPacket)new CharacterIconPacket().Parse(new PacketParser(raw, "CHI"));
        Assert.Equal(42, p.LoginId);
        Assert.Equal(0, p.Sheet);
        Assert.Equal(0, p.Graphic);
    }

    [Fact]
    public void Chi_PositiveSheetZeroGraphic_StoresAllFields()
    {
        var raw = "CHI42,2276,0";
        var p = (CharacterIconPacket)new CharacterIconPacket().Parse(new PacketParser(raw, "CHI"));
        Assert.Equal(42, p.LoginId);
        Assert.Equal(2276, p.Sheet);
        Assert.Equal(0, p.Graphic);
    }
}
