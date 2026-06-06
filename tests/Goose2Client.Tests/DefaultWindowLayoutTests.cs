using Godot;
using Goose2Client.UI;
using Xunit;

public class DefaultWindowLayoutTests
{
    [Fact] public void KnownWindows_HaveDistinctPositions()
    {
        var inv = DefaultWindowLayout.For("Inventory");
        var chr = DefaultWindowLayout.For("Character");
        Assert.NotEqual(inv, chr);
    }
    [Fact] public void Unknown_FallsBackToOrigin_Offset()
        => Assert.Equal(new Vector2(100, 100), DefaultWindowLayout.For("Nope"));
}
