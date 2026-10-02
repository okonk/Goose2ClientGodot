using Xunit;

namespace Goose2Client.Tests;

public class ChatTextTests
{
    [Fact]
    public void ToDisplay_MapsHeartCodeToGlyph()
    {
        Assert.Equal("a ♥ b", ChatText.ToDisplay("a ` b"));
    }

    [Fact]
    public void ToWire_MapsGlyphBackToHeartCode()
    {
        Assert.Equal("a ` b", ChatText.ToWire("a ♥ b"));
    }

    [Fact]
    public void WireRoundTrip_KeepsAsciiText()
    {
        Assert.Equal("hi `", ChatText.ToWire(ChatText.ToDisplay("hi `")));
    }
}
