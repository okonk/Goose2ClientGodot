using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class WindowResizeTests
{
    private static readonly Rect2 Start = new(100, 100, 400, 200);
    private static readonly Vector2 Min = new(260, 110);
    private static readonly Vector2 Canvas = new(1280, 720);

    [Theory]
    [InlineData(ResizeEdge.Right, 50, 0, 100, 100, 450, 200)]
    [InlineData(ResizeEdge.Left, -30, 0, 70, 100, 430, 200)]
    [InlineData(ResizeEdge.Bottom, 0, 40, 100, 100, 400, 240)]
    [InlineData(ResizeEdge.Top, 0, -40, 100, 60, 400, 240)]
    [InlineData(ResizeEdge.Top | ResizeEdge.Right, 50, -20, 100, 80, 450, 220)]
    [InlineData(ResizeEdge.Bottom | ResizeEdge.Left, -10, 10, 90, 100, 410, 210)]
    public void Apply_MovesOnlyTheDraggedEdges(ResizeEdge edge, float dx, float dy, float x, float y, float w, float h)
    {
        Assert.Equal(new Rect2(x, y, w, h), WindowResize.Apply(Start, edge, new Vector2(dx, dy), Min, Canvas));
    }

    [Fact]
    public void Apply_LeftPastMinimum_PinsRightEdge()
    {
        Assert.Equal(new Rect2(240, 100, 260, 200), WindowResize.Apply(Start, ResizeEdge.Left, new Vector2(200, 0), Min, Canvas));
    }

    [Fact]
    public void Apply_RightPastMinimum_ClampsToMinWidth()
    {
        Assert.Equal(new Rect2(100, 100, 260, 200), WindowResize.Apply(Start, ResizeEdge.Right, new Vector2(-500, 0), Min, Canvas));
    }

    [Fact]
    public void Apply_TopBeyondCanvas_ClampsToZero()
    {
        Assert.Equal(new Rect2(100, 0, 400, 300), WindowResize.Apply(Start, ResizeEdge.Top, new Vector2(0, -150), Min, Canvas));
    }

    [Fact]
    public void Apply_BottomBeyondCanvas_ClampsToCanvas()
    {
        Assert.Equal(new Rect2(100, 100, 400, 620), WindowResize.Apply(Start, ResizeEdge.Bottom, new Vector2(0, 1000), Min, Canvas));
    }

    [Fact]
    public void ScaledSize_RescalesSavedSizeByFactorRatio()
    {
        Assert.Equal(new Vector2(800, 400), WindowResize.ScaledSize(new Vector2(600, 300), 1.5f, 2f, Min, Canvas));
    }

    [Fact]
    public void ScaledSize_ZeroSavedFactor_TreatedAsOne()
    {
        Assert.Equal(new Vector2(1000, 416), WindowResize.ScaledSize(new Vector2(500, 208), 0f, 2f, Min, Canvas));
    }

    [Fact]
    public void ScaledSize_ClampsToMinAndCanvas()
    {
        Assert.Equal(new Vector2(260, 110), WindowResize.ScaledSize(new Vector2(100, 50), 1f, 1f, Min, Canvas));
        Assert.Equal(new Vector2(1280, 720), WindowResize.ScaledSize(new Vector2(2000, 1000), 1f, 1f, Min, Canvas));
    }
}
