using System.Text;
using Goose2.AssetConverter.Terrain;
using MapEditor.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetConverter.Tests.Terrain;

public readonly record struct TerrainPlacement(int X, int Y, int Sheet, int Graphic, int Layer = 0);

public readonly record struct TerrainSheetFrame(int Graphic, int X, int Y, int Width, int Height);

public sealed record TerrainFixtureMap(string FileName, int Width, int Height, IReadOnlyList<TerrainPlacement> Placements);

public sealed record TerrainFixtureSheet(int Sheet, int Width, int Height, IReadOnlyList<TerrainSheetFrame> Frames);

public sealed class TerrainFixtureDefinition
{
    private readonly List<TerrainFixtureMap> _maps = new();
    private readonly List<TerrainFixtureSheet> _sheets = new();

    public int TileSize { get; set; } = 32;

    public string? ManifestJson { get; set; }

    public IReadOnlyList<TerrainFixtureMap> Maps => _maps;

    public IReadOnlyList<TerrainFixtureSheet> Sheets => _sheets;

    public void AddMap(string fileName, int width, int height, params TerrainPlacement[] placements)
        => _maps.Add(new TerrainFixtureMap(fileName, width, height, placements));

    public void AddSheet(int sheet, int width, int height, params TerrainSheetFrame[] frames)
        => _sheets.Add(new TerrainFixtureSheet(sheet, width, height, frames));

    public void SetManifestJson(string json) => ManifestJson = json;

    public string BuildManifestJson()
    {
        var builder = new StringBuilder();
        builder.Append("{\"tileSize\":").Append(TileSize).Append(",\"sheets\":{");
        var firstSheet = true;
        foreach (var sheet in _sheets.OrderBy(s => s.Sheet))
        {
            if (!firstSheet)
            {
                builder.Append(',');
            }

            firstSheet = false;
            builder.Append('"').Append(sheet.Sheet).Append("\":{");
            var firstFrame = true;
            foreach (var frame in sheet.Frames.OrderBy(f => f.Graphic))
            {
                if (!firstFrame)
                {
                    builder.Append(',');
                }

                firstFrame = false;
                builder.Append('"').Append(frame.Graphic).Append("\":[")
                    .Append(frame.X).Append(',').Append(frame.Y).Append(',')
                    .Append(frame.Width).Append(',').Append(frame.Height).Append(']');
            }

            builder.Append('}');
        }

        builder.Append("}}");
        return builder.ToString();
    }
}

public static class TerrainFixtureBuilder
{
    public static TerrainFixture Create(TerrainFixtureDefinition definition)
    {
        var root = Path.Combine(Path.GetTempPath(), "ac_terrain_" + Guid.NewGuid().ToString("N"));
        var fixture = new TerrainFixture(root);
        fixture.WriteFromDefinition(definition);
        return fixture;
    }

    public static TerrainFixture Create(Action<TerrainFixtureDefinition> configure)
    {
        var definition = new TerrainFixtureDefinition();
        configure(definition);
        return Create(definition);
    }

    public static TerrainFixtureDefinition StandardDefinition()
    {
        var definition = new TerrainFixtureDefinition();
        definition.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100), new TerrainPlacement(1, 1, 1, 101));
        definition.AddMap("Map2.map", 2, 2, new TerrainPlacement(0, 0, 2, 200));
        definition.AddSheet(1, 64, 32, new TerrainSheetFrame(100, 0, 0, 32, 32), new TerrainSheetFrame(101, 32, 0, 32, 32));
        definition.AddSheet(2, 64, 32, new TerrainSheetFrame(200, 0, 0, 32, 32));
        return definition;
    }

    public static string FindMapFileName(int holdoutModulo, bool heldOut, int start = 1, int limit = 1000)
    {
        for (var i = start; i < start + limit; i++)
        {
            var name = $"Map{i}.map";
            if (TerrainHoldout.IsHeldOut("Assets/Maps/" + name, holdoutModulo) == heldOut)
            {
                return name;
            }
        }

        throw new InvalidOperationException("No map file name satisfies the holdout rule.");
    }
}

public sealed class TerrainFixture : IDisposable
{
    internal TerrainFixture(string root)
    {
        RepoRoot = root;
        Directory.CreateDirectory(MapsDirectory);
        Directory.CreateDirectory(SheetsDirectory);
    }

    public string RepoRoot { get; }

    public string MapsDirectory => Path.Combine(RepoRoot, "Assets", "Maps");

    public string SpritesDirectory => Path.Combine(RepoRoot, "Assets", "Sprites");

    public string SheetsDirectory => Path.Combine(SpritesDirectory, "sheets");

    internal void WriteFromDefinition(TerrainFixtureDefinition definition)
    {
        foreach (var map in definition.Maps)
        {
            WriteMap(map.FileName, map.Width, map.Height, map.Placements.ToArray());
        }

        foreach (var sheet in definition.Sheets)
        {
            WriteSheetPng(sheet.Sheet, sheet.Width, sheet.Height, sheet.Frames.ToArray());
        }

        WriteManifestRaw(definition.ManifestJson ?? definition.BuildManifestJson());
    }

    public void WriteMap(string fileName, int width, int height, params TerrainPlacement[] placements)
    {
        var document = MapDocument.Create(width, height);
        foreach (var placement in placements)
        {
            document.SetLayer(placement.X, placement.Y, placement.Layer, new MapTileLayer(placement.Sheet, placement.Graphic));
        }

        File.WriteAllBytes(Path.Combine(MapsDirectory, fileName), MapCodec.Encode(document));
    }

    public void WriteMapBytes(string fileName, byte[] bytes)
        => File.WriteAllBytes(Path.Combine(MapsDirectory, fileName), bytes);

    public void WriteInventory(params string[] fileNames)
        => TerrainMapInventory.Write(MapsDirectory, fileNames);

    public void WriteInventoryRaw(string content)
        => File.WriteAllText(Path.Combine(MapsDirectory, "terrain-map-inputs-v1.txt"), content);

    public void WriteManifestRaw(string json)
        => File.WriteAllText(Path.Combine(SpritesDirectory, "manifest.json"), json);

    public void WriteSheetPng(int sheet, int width, int height, params TerrainSheetFrame[] frames)
    {
        using var image = new Image<Rgba32>(width, height);
        foreach (var frame in frames)
        {
            var color = new Rgba32(
                (byte)((frame.Graphic * 7) % 251),
                (byte)((frame.Graphic * 13) % 251),
                (byte)((frame.Graphic * 29) % 251),
                255);
            for (var y = frame.Y; y < frame.Y + frame.Height; y++)
            {
                for (var x = frame.X; x < frame.X + frame.Width; x++)
                {
                    image[x, y] = color;
                }
            }
        }

        image.SaveAsPng(Path.Combine(SheetsDirectory, sheet + ".png"));
    }

    public void Dispose()
    {
        if (Directory.Exists(RepoRoot))
        {
            Directory.Delete(RepoRoot, recursive: true);
        }
    }
}
