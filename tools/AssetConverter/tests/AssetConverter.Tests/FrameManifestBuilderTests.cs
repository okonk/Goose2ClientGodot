using System.Text.Json;
using Goose2.AssetConverter;
using Goose2.AssetConverter.Manifest;
using Xunit;

namespace AssetConverter.Tests;

public class FrameManifestBuilderTests
{
    [Fact]
    public void Build_EmitsSheetGraphicRects_ForSheet1000()
    {
        string json = FrameManifestBuilder.Build(Paths.IllutiaData, onlyFileNumbers: new[] { 1000 });

        using var doc = JsonDocument.Parse(json);
        var sheets = doc.RootElement.GetProperty("sheets");
        var sheet1000 = sheets.GetProperty("1000");

        var first = sheet1000.GetProperty("108760");
        Assert.Equal(0,  first[0].GetInt32());
        Assert.Equal(0,  first[1].GetInt32());
        Assert.Equal(48, first[2].GetInt32());
        Assert.Equal(64, first[3].GetInt32());

        var last = sheet1000.GetProperty("108767");
        Assert.Equal(48,  last[0].GetInt32());
        Assert.Equal(192, last[1].GetInt32());

        Assert.Equal(8, sheet1000.EnumerateObject().Count());
    }

    [Fact]
    public void CombinedManifest_ContainsIllutiaAndRenumberedAsperetaSheets()
    {
        string json = FrameManifestBuilder.BuildCombined(Paths.IllutiaData, Paths.AsperetaData);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var sheets = doc.RootElement.GetProperty("sheets");

        Assert.True(sheets.TryGetProperty("1000", out _));      // illutia sheet still present
        Assert.True(sheets.TryGetProperty("20000", out var asp)); // renumbered aspereta sheet
        // every graphic key in an aspereta sheet is in the 700000+ range
        foreach (var g in asp.EnumerateObject())
            Assert.True(int.Parse(g.Name) >= 700000);
    }
}
