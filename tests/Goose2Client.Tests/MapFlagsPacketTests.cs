using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class MapFlagsPacketTests
{
    [Fact]
    public void NormalMap_ParsesFlags()
    {
        var p = (MapFlagsPacket)new MapFlagsPacket().Parse(new PacketParser("MFL0,1,1", "MFL"));
        Assert.False(p.PvPEnabled);
        Assert.True(p.ItemsEnabled);
        Assert.True(p.SpellsEnabled);
    }

    [Fact]
    public void PvpArena_ParsesFlags()
    {
        var p = (MapFlagsPacket)new MapFlagsPacket().Parse(new PacketParser("MFL1,1,1", "MFL"));
        Assert.True(p.PvPEnabled);
        Assert.True(p.ItemsEnabled);
        Assert.True(p.SpellsEnabled);
    }

    [Fact]
    public void RestrictedMap_ParsesFlags()
    {
        var p = (MapFlagsPacket)new MapFlagsPacket().Parse(new PacketParser("MFL0,0,0", "MFL"));
        Assert.False(p.PvPEnabled);
        Assert.False(p.ItemsEnabled);
        Assert.False(p.SpellsEnabled);
    }
}
