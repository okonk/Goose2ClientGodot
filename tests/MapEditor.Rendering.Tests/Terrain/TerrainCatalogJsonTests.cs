using System;
using System.IO;
using System.Text;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class TerrainCatalogJsonTests
{
    private const string GoldenDocument = """
        {
          "version": 1,
          "terrains": [
            {
              "id": "00000000-0000-0000-0000-000000000001",
              "name": "Grass",
              "color": null
            }
          ],
          "graphics": [
            {
              "sheet": 1,
              "graphic": 10,
              "center": "00000000-0000-0000-0000-000000000001",
              "north": null,
              "east": null,
              "south": null,
              "west": null,
              "northEast": null,
              "southEast": null,
              "southWest": null,
              "northWest": null
            }
          ]
        }
        """;

    private const string ShuffledDocument = """
        {
          "terrains": [
            { "color": "#aabbcc", "name": "Dirt", "id": "00000000-0000-0000-0000-000000000002" },
            { "color": null, "name": "Grass", "id": "00000000-0000-0000-0000-000000000001" },
            { "color": "#001122", "name": "Water", "id": "00000000-0000-0000-0000-000000000003" }
          ],
          "graphics": [
            {
              "northWest": null, "sheet": 2, "south": null, "west": null, "southEast": null,
              "north": null, "graphic": 5, "northEast": null, "southWest": null,
              "east": null, "center": "00000000-0000-0000-0000-000000000003"
            },
            {
              "west": null, "graphic": 10, "southWest": null, "north": null,
              "center": "00000000-0000-0000-0000-000000000001", "southEast": null,
              "northEast": null, "east": null, "south": null, "sheet": 1, "northWest": null
            },
            {
              "sheet": 1, "center": "00000000-0000-0000-0000-000000000002", "north": null,
              "east": "00000000-0000-0000-0000-000000000001", "graphic": 2, "south": null,
              "west": null, "northEast": null, "southEast": null, "southWest": null, "northWest": null
            }
          ],
          "version": 1
        }
        """;

    private const string CanonicalShuffledDocument = """
        {
          "version": 1,
          "terrains": [
            {
              "id": "00000000-0000-0000-0000-000000000001",
              "name": "Grass",
              "color": null
            },
            {
              "id": "00000000-0000-0000-0000-000000000002",
              "name": "Dirt",
              "color": "#AABBCC"
            },
            {
              "id": "00000000-0000-0000-0000-000000000003",
              "name": "Water",
              "color": "#001122"
            }
          ],
          "graphics": [
            {
              "sheet": 1,
              "graphic": 2,
              "center": "00000000-0000-0000-0000-000000000002",
              "north": null,
              "east": "00000000-0000-0000-0000-000000000001",
              "south": null,
              "west": null,
              "northEast": null,
              "southEast": null,
              "southWest": null,
              "northWest": null
            },
            {
              "sheet": 1,
              "graphic": 10,
              "center": "00000000-0000-0000-0000-000000000001",
              "north": null,
              "east": null,
              "south": null,
              "west": null,
              "northEast": null,
              "southEast": null,
              "southWest": null,
              "northWest": null
            },
            {
              "sheet": 2,
              "graphic": 5,
              "center": "00000000-0000-0000-0000-000000000003",
              "north": null,
              "east": null,
              "south": null,
              "west": null,
              "northEast": null,
              "southEast": null,
              "southWest": null,
              "northWest": null
            }
          ]
        }
        """;

    private static readonly Guid GrassId = new("00000000-0000-0000-0000-000000000001");

    [Fact]
    public void Serialize_GoldenDocument_ProducesExactUtf8Bytes()
    {
        var catalog = CreateGrassCatalog();
        var serialized = TerrainCatalogJson.Serialize(catalog);

        Assert.Equal(GoldenDocument + "\n", serialized);

        var bytes = Encoding.UTF8.GetBytes(serialized);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void Serialize_ShuffledInput_ProducesGoldenBytes()
    {
        var shuffled = TerrainCatalogJson.Parse(ShuffledDocument);
        var canonical = TerrainCatalogJson.Parse(CanonicalShuffledDocument + "\n");

        Assert.Equal(CanonicalShuffledDocument + "\n", TerrainCatalogJson.Serialize(shuffled));
        Assert.Equal(CanonicalShuffledDocument + "\n", TerrainCatalogJson.Serialize(canonical));
    }

    [Fact]
    public void RoundTrip_NullPeers_RemainNull()
    {
        var parsed = TerrainCatalogJson.Parse(GoldenDocument + "\n");
        var first = TerrainCatalogJson.Serialize(parsed);
        var reparsed = TerrainCatalogJson.Parse(first);
        var second = TerrainCatalogJson.Serialize(reparsed);

        Assert.Equal(first, second);
        Assert.True(parsed.Equals(reparsed));

        var pattern = parsed.Graphics[0].Pattern;
        Assert.Equal(GrassId, pattern.Center);
        Assert.Null(pattern.North);
        Assert.Null(pattern.East);
        Assert.Null(pattern.South);
        Assert.Null(pattern.West);
        Assert.Null(pattern.NorthEast);
        Assert.Null(pattern.SouthEast);
        Assert.Null(pattern.SouthWest);
        Assert.Null(pattern.NorthWest);
    }

    [Fact]
    public void Parse_ValidDocument_ReturnsCatalog()
    {
        var catalog = TerrainCatalogJson.Parse(GoldenDocument);

        Assert.Equal(1, catalog.Terrains.Count);
        Assert.Equal(GrassId, catalog.Terrains[0].Id);
        Assert.Equal("Grass", catalog.Terrains[0].Name);
        Assert.Null(catalog.Terrains[0].ColorOverride);

        Assert.Equal(1, catalog.Graphics.Count);
        Assert.Equal(new TerrainGraphicReference(1, 10), catalog.Graphics[0].Reference);
        Assert.Equal(GrassId, catalog.Graphics[0].Pattern.Center);
    }

    [Fact]
    public void Parse_ColorOverride_ParsesRgb()
    {
        var json = Document(1, "[ { \"id\": \"00000000-0000-0000-0000-000000000001\", \"name\": \"Grass\", \"color\": \"#1a2b3c\" } ]", "[]");

        var catalog = TerrainCatalogJson.Parse(json);

        Assert.Equal(new TerrainColor(0x1A, 0x2B, 0x3C), catalog.Terrains[0].ColorOverride);
    }

    [Fact]
    public void Parse_CenterlessPattern_IsLegalAtSyntaxLayer()
    {
        var json = Document(1, "[]", "[ { " + GraphicProperties(1, 10, "\"center\": null") + " } ]");

        var catalog = TerrainCatalogJson.Parse(json);

        Assert.Null(catalog.Graphics[0].Pattern.Center);
    }

    [Fact]
    public void Parse_EmptyArrays_ReturnsEmptyCatalog()
    {
        var catalog = TerrainCatalogJson.Parse(Document(1, "[]", "[]"));

        Assert.Empty(catalog.Terrains);
        Assert.Empty(catalog.Graphics);
    }

    [Fact]
    public void Parse_UnsupportedVersion_ThrowsTypedFailure()
    {
        var ex = Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(Document(2, "[]", "[]")));

        Assert.Contains("<memory>", ex.Message);
    }

    [Fact]
    public void Parse_Malformed_ThrowsTypedFailure()
    {
        var ex = Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse("{ \"version\": 1,"));

        Assert.Contains("<memory>", ex.Message);
    }

    [Fact]
    public void Parse_NonObjectRoot_ThrowsTypedFailure()
    {
        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse("[1, 2]"));
    }

    [Fact]
    public void Parse_ErrorMessageIncludesSourcePath()
    {
        var ex = Assert.Throws<TerrainCatalogFormatException>(
            () => TerrainCatalogJson.Parse("{", "/tmp/terrain.json"));

        Assert.Equal("/tmp/terrain.json", ex.Path);
        Assert.Contains("/tmp/terrain.json", ex.Message);
    }

    [Fact]
    public void Parse_MissingVersion_ThrowsTypedFailure()
    {
        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse("{ \"terrains\": [], \"graphics\": [] }"));
    }

    [Fact]
    public void Parse_MissingTerrains_ThrowsTypedFailure()
    {
        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse("{ \"version\": 1, \"graphics\": [] }"));
    }

    [Fact]
    public void Parse_MissingGraphics_ThrowsTypedFailure()
    {
        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse("{ \"version\": 1, \"terrains\": [] }"));
    }

    [Fact]
    public void Parse_VersionNotInteger_ThrowsTypedFailure()
    {
        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse("{ \"version\": 1.5, \"terrains\": [], \"graphics\": [] }"));
        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse("{ \"version\": \"1\", \"terrains\": [], \"graphics\": [] }"));
    }

    [Fact]
    public void Parse_DuplicateRootProperty_ThrowsTypedFailure()
    {
        Assert.Throws<TerrainCatalogFormatException>(
            () => TerrainCatalogJson.Parse("{ \"version\": 1, \"version\": 2, \"terrains\": [], \"graphics\": [] }"));
    }

    [Fact]
    public void Parse_DuplicateTerrainProperty_ThrowsTypedFailure()
    {
        var json = Document(1, "[ { \"id\": \"00000000-0000-0000-0000-000000000001\", \"id\": \"00000000-0000-0000-0000-000000000001\", \"name\": \"Grass\", \"color\": null } ]", "[]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_TerrainMissingProperty_ThrowsTypedFailure()
    {
        var json = Document(1, "[ { \"id\": \"00000000-0000-0000-0000-000000000001\", \"color\": null } ]", "[]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_TerrainUnknownProperty_ThrowsTypedFailure()
    {
        var json = Document(1, "[ { \"id\": \"00000000-0000-0000-0000-000000000001\", \"name\": \"Grass\", \"color\": null, \"extra\": 1 } ]", "[]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_RootUnknownProperty_ThrowsTypedFailure()
    {
        Assert.Throws<TerrainCatalogFormatException>(
            () => TerrainCatalogJson.Parse("{ \"version\": 1, \"terrains\": [], \"graphics\": [], \"extra\": true }"));
    }

    [Fact]
    public void Parse_GraphicUnknownProperty_ThrowsTypedFailure()
    {
        var json = Document(1, "[]", "[ { " + GraphicProperties(1, 10, "\"center\": null, \"extra\": 1") + " } ]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_GraphicMissingProperty_ThrowsTypedFailure()
    {
        var properties = "\"sheet\": 1, \"graphic\": 10, \"center\": null, \"north\": null, \"east\": null, \"south\": null, "
            + "\"northEast\": null, \"southEast\": null, \"southWest\": null, \"northWest\": null";
        var json = Document(1, "[]", "[ { " + properties + " } ]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_TerrainNotObject_ThrowsTypedFailure()
    {
        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(Document(1, "[ 1 ]", "[]")));
    }

    [Fact]
    public void Parse_GraphicNotObject_ThrowsTypedFailure()
    {
        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(Document(1, "[]", "[ 1 ]")));
    }

    [Fact]
    public void Parse_NameNotString_ThrowsTypedFailure()
    {
        var json = Document(1, "[ { \"id\": \"00000000-0000-0000-0000-000000000001\", \"name\": 3, \"color\": null } ]", "[]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_ColorNotString_ThrowsTypedFailure()
    {
        var json = Document(1, "[ { \"id\": \"00000000-0000-0000-0000-000000000001\", \"name\": \"Grass\", \"color\": 1 } ]", "[]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("123456")]
    [InlineData("#GGHHII")]
    [InlineData("# 12345")]
    [InlineData("#1 2345")]
    [InlineData("#12 345")]
    [InlineData("#1234 5")]
    public void Parse_InvalidColor_ThrowsTypedFailure(string color)
    {
        var json = Document(1, $"[ {{ \"id\": \"00000000-0000-0000-0000-000000000001\", \"name\": \"Grass\", \"color\": \"{color}\" }} ]", "[]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_InvalidTerrainGuid_ThrowsTypedFailure()
    {
        var json = Document(1, "[ { \"id\": \"not-a-guid\", \"name\": \"Grass\", \"color\": null } ]", "[]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_InvalidPeerGuid_ThrowsTypedFailure()
    {
        var json = Document(1, "[]", "[ { " + GraphicProperties(1, 10, "\"center\": \"nope\"") + " } ]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_PeerNotString_ThrowsTypedFailure()
    {
        var json = Document(1, "[]", "[ { " + GraphicProperties(1, 10, "\"center\": 5") + " } ]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_SheetNotInteger_ThrowsTypedFailure()
    {
        var json = Document(1, "[]", "[ { " + GraphicProperties("\"1\"", 10, "\"center\": null") + " } ]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Parse_SheetIntegerOverflow_ThrowsTypedFailure()
    {
        var json = Document(1, "[]", "[ { " + GraphicProperties(2147483648, 10, "\"center\": null") + " } ]");

        Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Parse(json));
    }

    [Fact]
    public void Load_ReadsFileAndReportsPathOnFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, GoldenDocument + "\n");
        try
        {
            var catalog = TerrainCatalogJson.Load(path);

            Assert.Equal(1, catalog.Terrains.Count);
            Assert.Equal("Grass", catalog.Terrains[0].Name);

            File.WriteAllText(path, "{ broken");
            var ex = Assert.Throws<TerrainCatalogFormatException>(() => TerrainCatalogJson.Load(path));

            Assert.Contains(path, ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_PropagatesFileNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N") + ".json");

        Assert.Throws<FileNotFoundException>(() => TerrainCatalogJson.Load(path));
    }

    [Fact]
    public void Serialize_DoesNotMutateSourceCatalog()
    {
        var first = new TerrainDefinition(new Guid("00000000-0000-0000-0000-000000000001"), "First", null);
        var second = new TerrainDefinition(new Guid("00000000-0000-0000-0000-000000000002"), "Second", null);
        var graphicsFirst = new TerrainGraphicDefinition(new TerrainGraphicReference(1, 1), new TerrainPattern());
        var graphicsSecond = new TerrainGraphicDefinition(new TerrainGraphicReference(1, 2), new TerrainPattern());
        var catalog = new TerrainCatalog(new[] { second, first }, new[] { graphicsSecond, graphicsFirst });

        TerrainCatalogJson.Serialize(catalog);

        Assert.Equal(new[] { second, first }, catalog.Terrains);
        Assert.Equal(new[] { graphicsSecond, graphicsFirst }, catalog.Graphics);
    }

    private static TerrainCatalog CreateGrassCatalog()
    {
        return new TerrainCatalog(
            new[] { new TerrainDefinition(GrassId, "Grass", null) },
            new[]
            {
                new TerrainGraphicDefinition(new TerrainGraphicReference(1, 10), new TerrainPattern(Center: GrassId))
            });
    }

    private static string Document(object version, string terrains, string graphics)
        => $"{{ \"version\": {version}, \"terrains\": {terrains}, \"graphics\": {graphics} }}";

    private static string GraphicProperties(object sheet, int graphic, string property)
    {
        return "\"sheet\": " + sheet + ", \"graphic\": " + graphic + ", " + property
            + ", \"north\": null, \"east\": null, \"south\": null, \"west\": null, "
            + "\"northEast\": null, \"southEast\": null, \"southWest\": null, \"northWest\": null";
    }
}
