using Goose2Client;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class MapAmbiencePacketTests
{
    [Fact]
    public void Amb_ParsesOutsideRain()
    {
        var p = (MapAmbiencePacket)new MapAmbiencePacket().Parse(new PacketParser("AMB0,700,151587327,1321471,540000,1440000,1", "AMB"));
        Assert.Equal(0f, p.StartAlpha);
        Assert.Equal(0.7f, p.EndAlpha);
        Assert.Equal(0x090909FFu, p.TintColor);
        Assert.Equal(0x001429FFu, p.FillColor);
        Assert.Equal(540000, p.ElapsedMs);
        Assert.Equal(1440000, p.DurationMs);
        Assert.Equal(WeatherType.Rain, p.Weather);
    }

    [Fact]
    public void Amb_ParsesNone()
    {
        var p = (MapAmbiencePacket)new MapAmbiencePacket().Parse(new PacketParser("AMB0,0,151587327,1321471,0,0,0", "AMB"));
        Assert.Equal(0f, p.StartAlpha);
        Assert.Equal(0f, p.EndAlpha);
        Assert.Equal(0, p.ElapsedMs);
        Assert.Equal(0, p.DurationMs);
        Assert.Equal(WeatherType.Clear, p.Weather);
    }

    [Fact]
    public void Amb_ColorAboveIntMax()
    {
        var p = (MapAmbiencePacket)new MapAmbiencePacket().Parse(new PacketParser("AMB0,0,4294967295,1321471,0,0,0", "AMB"));
        Assert.Equal(0xFFFFFFFFu, p.TintColor);
    }

    [Fact]
    public void Amb_UnknownWeather_IsClear()
    {
        var p = (MapAmbiencePacket)new MapAmbiencePacket().Parse(new PacketParser("AMB0,0,151587327,1321471,0,0,9", "AMB"));
        Assert.Equal(WeatherType.Clear, p.Weather);
    }
}
