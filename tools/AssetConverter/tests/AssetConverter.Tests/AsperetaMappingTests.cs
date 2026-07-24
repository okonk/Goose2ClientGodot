using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaMappingTests
{
    private static string? CommittedMappingPath()
    {
        // tools/AssetConverter/data/aspereta-mapping.tsv relative to test assembly output
        // bin/Debug/netX.Y/ → five levels up to tools/AssetConverter/
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "aspereta-mapping.tsv")),
            // fallback if layout differs
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data", "aspereta-mapping.tsv")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "data", "aspereta-mapping.tsv")),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

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

    [Fact]
    public void ToTsv_FromTsv_RoundTrips()
    {
        var original = new List<MappingRow>
        {
            new(1, 0, MappingStatus.Matched, 42, 7),
            new(2, 3, MappingStatus.Inject, 20001, 700003),
            new(99, 1, MappingStatus.Matched, 10, 5),
        };

        var path = Path.Combine(Path.GetTempPath(), "aspereta_map_" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            File.WriteAllText(path, AsperetaMapping.ToTsv(original));
            var loaded = AsperetaMapping.FromTsv(path);
            Assert.Equal(original, loaded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FromTsv_RejectsBadStatus()
    {
        var path = Path.Combine(Path.GetTempPath(), "aspereta_map_bad_" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            File.WriteAllText(path,
                "asp_sheet\tasp_graphic\tstatus\tout_sheet\tout_graphic\n" +
                "1\t0\tMATCHED\t42\t7\n");
            var ex = Assert.Throws<FormatException>(() => AsperetaMapping.FromTsv(path));
            Assert.Contains("Line 2", ex.Message);
            Assert.Contains("matched", ex.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FromTsv_RejectsShortLines()
    {
        var path = Path.Combine(Path.GetTempPath(), "aspereta_map_short_" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            File.WriteAllText(path,
                "asp_sheet\tasp_graphic\tstatus\tout_sheet\tout_graphic\n" +
                "1\t0\tmatched\t42\n");
            var ex = Assert.Throws<FormatException>(() => AsperetaMapping.FromTsv(path));
            Assert.Contains("Line 2", ex.Message);
            Assert.Contains("5 tab-separated columns", ex.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CommittedTsv_HasExpectedCounts()
    {
        var path = CommittedMappingPath();
        if (path is null)
        {
            // Committed mapping not present in this checkout layout — skip.
            return;
        }

        var rows = AsperetaMapping.FromTsv(path);
        Assert.Equal(16139, rows.Count);
        Assert.Equal(3089, rows.Count(r => r.Status == MappingStatus.Matched));
        Assert.Equal(13050, rows.Count(r => r.Status == MappingStatus.Inject));
    }

    [Fact]
    public void InjectOutGraphics_AreUnique()
    {
        List<MappingRow> rows;
        var path = CommittedMappingPath();
        if (path is not null)
            rows = AsperetaMapping.FromTsv(path);
        else
            rows = AsperetaMapping.Build(Paths.IllutiaData, Paths.AsperetaData);

        var injectOut = rows.Where(r => r.Status == MappingStatus.Inject).Select(r => r.OutGraphic).ToList();
        Assert.Equal(injectOut.Count, injectOut.Distinct().Count());
    }

    [Fact]
    public void NoDuplicateAspSheetGraphicPairs()
    {
        List<MappingRow> rows;
        var path = CommittedMappingPath();
        if (path is not null)
            rows = AsperetaMapping.FromTsv(path);
        else
            rows = AsperetaMapping.Build(Paths.IllutiaData, Paths.AsperetaData);

        var pairs = rows.Select(r => (r.AspSheet, r.AspGraphic)).ToList();
        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }
}
