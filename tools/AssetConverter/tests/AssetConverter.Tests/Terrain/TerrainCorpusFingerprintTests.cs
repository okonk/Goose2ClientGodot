using Goose2.AssetConverter.Terrain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetConverter.Tests.Terrain;

public class TerrainCorpusFingerprintTests
{
    [Fact]
    public void Compute_EquivalentRootsCreationOrderAndTimestampsHaveSameFingerprint()
    {
        var definition = TerrainFixtureBuilder.StandardDefinition();
        using var first = TerrainFixtureBuilder.Create(definition);
        first.WriteInventory("Map1.map", "Map2.map");
        var firstFingerprint = TerrainCorpusFingerprint.Compute(first.RepoRoot);
        Assert.Matches("^sha256:[0-9a-f]{64}$", firstFingerprint);

        using var second = TerrainFixtureBuilder.Create(definition);
        second.WriteManifestRaw(definition.ManifestJson ?? definition.BuildManifestJson());
        foreach (var sheet in definition.Sheets)
        {
            second.WriteSheetPng(sheet.Sheet, sheet.Width, sheet.Height, sheet.Frames.ToArray());
        }

        foreach (var map in definition.Maps)
        {
            second.WriteMap(map.FileName, map.Width, map.Height, map.Placements.ToArray());
        }

        second.WriteInventory("Map1.map", "Map2.map");
        Assert.Equal(firstFingerprint, TerrainCorpusFingerprint.Compute(second.RepoRoot));

        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        foreach (var file in Directory.EnumerateFiles(second.RepoRoot, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, timestamp);
        }

        Assert.Equal(firstFingerprint, TerrainCorpusFingerprint.Compute(second.RepoRoot));

        var movedRoot = second.RepoRoot + "_moved";
        Directory.Move(second.RepoRoot, movedRoot);
        try
        {
            Assert.Equal(firstFingerprint, TerrainCorpusFingerprint.Compute(movedRoot));
        }
        finally
        {
            if (Directory.Exists(movedRoot))
            {
                Directory.Delete(movedRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Compute_UnlistedMapAndUnreferencedPngDoNotChangeFingerprint()
    {
        using var fixture = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition());
        fixture.WriteInventory("Map1.map", "Map2.map");
        var baseline = TerrainCorpusFingerprint.Compute(fixture.RepoRoot);

        fixture.WriteMap("Map9.map", 2, 2, new TerrainPlacement(0, 0, 9, 999));
        fixture.WriteSheetPng(999, 32, 32);

        Assert.Equal(baseline, TerrainCorpusFingerprint.Compute(fixture.RepoRoot));
    }

    [Fact]
    public void Compute_InventoryListedMapManifestOrRelevantPngByteChangeChangesFingerprint()
    {
        var definition = TerrainFixtureBuilder.StandardDefinition();
        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory("Map1.map", "Map2.map");
        var baseline = TerrainCorpusFingerprint.Compute(fixture.RepoRoot);

        fixture.WriteMap("Map3.map", 2, 2, new TerrainPlacement(0, 0, 1, 100));
        fixture.WriteInventory("Map1.map", "Map2.map", "Map3.map");
        Assert.NotEqual(baseline, TerrainCorpusFingerprint.Compute(fixture.RepoRoot));
        File.Delete(Path.Combine(fixture.MapsDirectory, "Map3.map"));
        fixture.WriteInventory("Map1.map", "Map2.map");
        Assert.Equal(baseline, TerrainCorpusFingerprint.Compute(fixture.RepoRoot));

        fixture.WriteMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100));
        Assert.NotEqual(baseline, TerrainCorpusFingerprint.Compute(fixture.RepoRoot));
        fixture.WriteMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100), new TerrainPlacement(1, 1, 1, 101));
        Assert.Equal(baseline, TerrainCorpusFingerprint.Compute(fixture.RepoRoot));

        fixture.WriteManifestRaw(
            "{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[32,0,32,32],\"101\":[32,0,32,32]},\"2\":{\"200\":[0,0,32,32]}}}");
        Assert.NotEqual(baseline, TerrainCorpusFingerprint.Compute(fixture.RepoRoot));
        fixture.WriteManifestRaw(definition.BuildManifestJson());
        Assert.Equal(baseline, TerrainCorpusFingerprint.Compute(fixture.RepoRoot));

        fixture.WriteSheetPng(1, 64, 32, new TerrainSheetFrame(100, 0, 0, 32, 32), new TerrainSheetFrame(101, 0, 0, 32, 32));
        Assert.NotEqual(baseline, TerrainCorpusFingerprint.Compute(fixture.RepoRoot));

        fixture.WriteSheetPng(1, 64, 32, new TerrainSheetFrame(100, 0, 0, 32, 32), new TerrainSheetFrame(101, 32, 0, 32, 32));
        var sheetPath = Path.Combine(fixture.SheetsDirectory, "1.png");
        var originalPng = File.ReadAllBytes(sheetPath);
        using var image = new Image<Rgba32>(64, 32);
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                image[x, y] = x < 32 ? new Rgba32(198, 45, 139, 255) : new Rgba32(1, 2, 3, 255);
            }
        }

        image.SaveAsPng(sheetPath);
        var replacedPng = File.ReadAllBytes(sheetPath);
        Assert.Equal(originalPng.Length, replacedPng.Length);
        Assert.NotEqual(originalPng, replacedPng);
        Assert.NotEqual(baseline, TerrainCorpusFingerprint.Compute(fixture.RepoRoot));
    }

    [Fact]
    public void Compute_SettingsChangeDoesNotChangeCorpusFingerprint()
    {
        using var fixture = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition());
        fixture.WriteInventory("Map1.map", "Map2.map");
        var settingsPath = Path.Combine(fixture.RepoRoot, "terrain-settings.json");
        File.WriteAllText(settingsPath, "{\"holdoutModulo\":7}");
        var before = TerrainCorpusFingerprint.Compute(fixture.RepoRoot);

        File.WriteAllText(settingsPath, "{\"holdoutModulo\":97}");

        Assert.Equal(before, TerrainCorpusFingerprint.Compute(fixture.RepoRoot));
    }
}
