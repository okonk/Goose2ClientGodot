using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Goose2Client;
using Xunit;

namespace Goose2Client.Tests;

public class SpellShapesTests
{
    private sealed record ShapeCase(string Name, string Shape, int Size, string Facing, bool Legacy, int[][] Tiles);

    private static List<ShapeCase> Cases() => JsonSerializer.Deserialize<List<ShapeCase>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "shape-tiles.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public void Matches_every_server_case()
    {
        foreach (var c in Cases())
        {
            var expected = c.Tiles.Select(t => (t[0], t[1])).ToList();
            var actual = SpellShapes.Tiles(Enum.Parse<SpellShape>(c.Shape), c.Size, 10, 10, Enum.Parse<Direction>(c.Facing));
            Assert.True(expected.SequenceEqual(actual), $"{c.Name}: got {string.Join(" ", actual)}");
        }
    }

    [Fact]
    public void Random_has_no_predictable_tiles()
    {
        Assert.Empty(SpellShapes.Tiles(SpellShape.Random, 2, 10, 10, Direction.Down));
    }

    [Theory]
    [InlineData(SpellShape.Target, false)]
    [InlineData(SpellShape.Random, false)]
    [InlineData(SpellShape.Area, true)]
    [InlineData(SpellShape.LineFront, true)]
    public void Only_predictable_area_shapes_preview(SpellShape shape, bool previews)
    {
        Assert.Equal(previews, SpellShapes.HasPreview(shape));
    }
}
