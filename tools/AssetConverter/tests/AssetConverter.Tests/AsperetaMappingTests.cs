using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaMappingTests
{
    [Fact]
    public void SheetNumbering_IsRankBased()
    {
        var sheets = AsperetaSheets.Load(Paths.AsperetaData);
        Assert.Equal(462, sheets.Count);
        var ordered = sheets.Keys.OrderBy(n => n).ToList();
        Assert.Equal(20000, sheets[ordered[0]].NewSheetNumber);
        Assert.Equal(20000 + 461, sheets[ordered[^1]].NewSheetNumber);
    }

    [Fact]
    public void FullDatasets_ProduceKnownMatchCounts()
    {
        var rows = AsperetaMapping.Build(Paths.IllutiaData, Paths.AsperetaData);

        // Dataset-specific goldens measured against local Illutia + Aspereta data dirs.
        // Plan (early estimate): 16139 rows, 3079 matched, 13060 inject.
        // Measured (462 graphic sheets; near-black keying): 16139 total, 3089 matched, 13050 inject.
        // Total matches plan; +10 matched / -10 inject vs early estimate — pinned to actual Build().
        Assert.Equal(16139, rows.Count);
        Assert.Equal(3089, rows.Count(r => r.Status == MappingStatus.Matched));
        Assert.Equal(13050, rows.Count(r => r.Status == MappingStatus.Inject));

        var inject = rows.First(r => r.Status == MappingStatus.Inject);
        Assert.Equal(700000 + inject.AspGraphic, inject.OutGraphic);
        Assert.InRange(inject.OutSheet, 20000, 20461);
    }
}
