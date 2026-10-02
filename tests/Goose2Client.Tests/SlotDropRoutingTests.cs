using System;
using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class SlotDropRoutingTests
{
    private static readonly Rect2[] Grid =
    {
        new(0, 0, 32, 32),
        new(33, 0, 32, 32),
        new(0, 33, 32, 32),
        new(33, 33, 32, 32),
    };

    [Fact]
    public void PointInsideSlot_ReturnsThatSlot()
    {
        Assert.Equal(2, SlotDropRouting.NearestSlot(Grid, new Vector2(10, 40)));
    }

    [Fact]
    public void PointInGapBetweenSlots_ReturnsClosestSlot()
    {
        Assert.Equal(1, SlotDropRouting.NearestSlot(Grid, new Vector2(32.9f, 16)));
        Assert.Equal(2, SlotDropRouting.NearestSlot(Grid, new Vector2(16, 32.9f)));
    }

    [Fact]
    public void PointOutsideGrid_ReturnsNearestCenter()
    {
        Assert.Equal(3, SlotDropRouting.NearestSlot(Grid, new Vector2(200, 200)));
        Assert.Equal(0, SlotDropRouting.NearestSlot(Grid, new Vector2(-50, -50)));
    }

    [Fact]
    public void DegenerateRects_AreSkipped()
    {
        Rect2[] rects = { new(0, 0, 0, 0), new(100, 100, 32, 32) };
        Assert.Equal(1, SlotDropRouting.NearestSlot(rects, new Vector2(0, 0)));
    }

    [Fact]
    public void EmptyList_ReturnsMinusOne()
    {
        Assert.Equal(-1, SlotDropRouting.NearestSlot(Array.Empty<Rect2>(), Vector2.Zero));
    }
}
