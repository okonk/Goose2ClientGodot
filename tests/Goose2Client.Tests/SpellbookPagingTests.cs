using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class SpellbookPagingTests
{
    [Fact]
    public void Locate_MiddleSlot_ReturnsCorrectPageAndSlot()
    {
        // globalIndex=35, slotsPerPage=30, pageCount=3 → page 1, slot 5
        var result = SpellbookPaging.Locate(35, 30, 3);
        Assert.NotNull(result);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(5, result.Value.Slot);
    }

    [Fact]
    public void Locate_OutOfRangePage_ReturnsNull()
    {
        // globalIndex=90, slotsPerPage=30, pageCount=3 → page 3 >= count 3 → null
        var result = SpellbookPaging.Locate(90, 30, 3);
        Assert.Null(result);
    }

    [Fact]
    public void FirstEmpty_Forward_FirstSlotOfNextPage()
    {
        // fromIndex=5 (page 0), forward=true, 2 pages → first empty in page 1 is slot 30
        var result = SpellbookPaging.FirstEmpty(5, forward: true, 30, 2, _ => false);
        Assert.Equal(30, result);
    }

    [Fact]
    public void FirstEmpty_Backward_SkipsOccupied()
    {
        // fromIndex=35 (page 1), forward=false → scan page 0; slot 0 occupied, slot 1 empty
        var result = SpellbookPaging.FirstEmpty(35, forward: false, 30, 2, g => g == 0);
        Assert.Equal(1, result);
    }

    [Fact]
    public void FirstEmpty_NoPageAfter_ReturnsNull()
    {
        // fromIndex=5 (page 0), forward=true, only 1 page → no pages after → null
        var result = SpellbookPaging.FirstEmpty(5, forward: true, 30, 1, _ => false);
        Assert.Null(result);
    }
}
