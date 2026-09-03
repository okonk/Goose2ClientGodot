using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapEditor.App.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class TileSheetFilterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-tile-filter-").FullName;

    public void Dispose()
        => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        Assert.Null(TileSheetFilter.Load(_directory));
    }

    [Fact]
    public void Load_ValidArray_ReturnsSet()
    {
        File.WriteAllText(Path.Combine(_directory, TileSheetFilter.FileName), "[1, 53, 20000]");

        IReadOnlySet<int>? filter = TileSheetFilter.Load(_directory);

        Assert.NotNull(filter);
        Assert.Equal(new[] { 1, 53, 20000 }, filter!.OrderBy(x => x));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1, \"x\"]")]
    public void Load_InvalidFile_ReturnsNull(string content)
    {
        File.WriteAllText(Path.Combine(_directory, TileSheetFilter.FileName), content);

        Assert.Null(TileSheetFilter.Load(_directory));
    }

    [Fact]
    public void Apply_NullFilter_ReturnsOriginalList()
    {
        var sheetIds = new[] { 1, 2, 3 };

        Assert.Same(sheetIds, TileSheetFilter.Apply(sheetIds, null));
    }

    [Fact]
    public void Apply_Filter_ReturnsIntersectionInOriginalOrder()
    {
        var sheetIds = new[] { 3, 1, 4, 2 };
        var filter = new HashSet<int> { 2, 9, 1 };

        Assert.Equal(new[] { 1, 2 }, TileSheetFilter.Apply(sheetIds, filter));
    }
}
