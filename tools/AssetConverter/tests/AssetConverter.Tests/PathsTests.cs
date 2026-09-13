using Xunit;

namespace AssetConverter.Tests;

public class PathsTests
{
    [Fact]
    public void ItemTileSheets_Default_ResolvesToCheckedInFile()
    {
        var path = Goose2.AssetConverter.Paths.ItemTileSheets;
        Assert.EndsWith("tools/AssetConverter/data/item-tile-sheets.json", path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ItemTileSheets_EnvOverride_ReturnsExactValue()
    {
        var temp = Path.Combine(Path.GetTempPath(), "item-tile-sheets.json");
        Environment.SetEnvironmentVariable("ITEM_TILE_SHEETS", temp);
        try
        {
            Assert.Equal(temp, Goose2.AssetConverter.Paths.ItemTileSheets);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ITEM_TILE_SHEETS", null);
        }
    }
}
