using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class WindowOcclusionTests
{
    private static readonly Rect2 Window = new(100, 100, 200, 120);

    private static bool Covered(Rect2[] rects, Vector2 point) => WindowOcclusion.IsPointInsideAny(rects, point);

    [Fact]
    public void NoWindows_CoverNothing()
        => Assert.False(Covered(System.Array.Empty<Rect2>(), new Vector2(150, 150)));

    [Fact]
    public void PointInsideWindow_IsCovered()
        => Assert.True(Covered(new[] { Window }, new Vector2(150, 150)));

    [Theory]
    [InlineData(100, 100)]
    [InlineData(299, 219)]
    [InlineData(150, 150)]
    public void PointOnWindow_IsCovered(float x, float y)
        => Assert.True(Covered(new[] { Window }, new Vector2(x, y)));

    [Theory]
    [InlineData(99, 150)]
    [InlineData(150, 99)]
    [InlineData(300, 150)]
    [InlineData(150, 220)]
    [InlineData(0, 0)]
    public void PointOffWindow_IsNotCovered(float x, float y)
        => Assert.False(Covered(new[] { Window }, new Vector2(x, y)));

    [Fact]
    public void AnyWindowInList_Covers()
        => Assert.True(Covered(new[] { Window, new Rect2(0, 0, 40, 40) }, new Vector2(20, 20)));
}
