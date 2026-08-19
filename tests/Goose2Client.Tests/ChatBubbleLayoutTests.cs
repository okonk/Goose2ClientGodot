using Godot;
using Goose2Client.Overlays;
using Xunit;

public class ChatBubbleLayoutTests
{
    [Fact]
    public void ClampWidth_BelowMaxWidth_ReturnsInput()
    {
        Assert.Equal(100f, ChatBubbleLayout.ClampWidth(100f));
    }

    [Fact]
    public void ClampWidth_AtMaxWidth_ReturnsMaxWidth()
    {
        Assert.Equal(250f, ChatBubbleLayout.ClampWidth(250f));
    }

    [Fact]
    public void ClampWidth_AboveMaxWidth_ClampsTo250()
    {
        Assert.Equal(250f, ChatBubbleLayout.ClampWidth(400f));
    }

    [Fact]
    public void BackgroundSize_AddsPadding()
    {
        var size = ChatBubbleLayout.BackgroundSize(new Vector2(100f, 20f));
        Assert.Equal(114f, size.X); // 100 + 2*7
        Assert.Equal(30f, size.Y); // 20 + 2*5
    }

    [Fact]
    public void Lifetime_ExpiresAfter3Seconds()
    {
        var l = new OverlayLifetime(ChatBubbleLayout.LifetimeSeconds);
        l.Advance(2.99);
        Assert.False(l.Expired);
        l.Advance(0.02);
        Assert.True(l.Expired);
    }
}
