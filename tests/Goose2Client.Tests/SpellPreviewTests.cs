using Goose2Client;
using Xunit;

namespace Goose2Client.Tests;

public class SpellPreviewTests
{
    private static SpellInfo Spell(SpellShape shape, int size, bool ground) =>
        new() { Name = "S", Shape = shape, ShapeSize = size, GroundTargetable = ground };

    [Fact]
    public void Ground_spells_anchor_on_the_hovered_tile()
    {
        var anchor = SpellPreview.Anchor(Spell(SpellShape.Area, 1, true), hover: (5, 6), target: (1, 1));

        Assert.Equal((5, 6), anchor);
    }

    [Fact]
    public void Other_spells_anchor_on_the_reticle_target()
    {
        var anchor = SpellPreview.Anchor(Spell(SpellShape.Area, 1, false), hover: (5, 6), target: (1, 1));

        Assert.Equal((1, 1), anchor);
    }

    [Fact]
    public void Ground_spells_fall_back_to_the_target_with_no_hover()
    {
        Assert.Equal((1, 1), SpellPreview.Anchor(Spell(SpellShape.Area, 1, true), hover: null, target: (1, 1)));
    }

    [Fact]
    public void Tiles_are_clipped_to_the_map()
    {
        var tiles = SpellPreview.Tiles(Spell(SpellShape.Area, 1, true), anchor: (0, 0), Direction.Down, mapSize: (10, 10));

        Assert.Equal(new[] { (0, 0), (1, 0), (0, 1), (1, 1) }, tiles);
    }

    [Fact]
    public void Shapes_without_a_preview_give_no_tiles()
    {
        Assert.Empty(SpellPreview.Tiles(Spell(SpellShape.Target, 0, false), (5, 5), Direction.Down, (10, 10)));
        Assert.Empty(SpellPreview.Tiles(Spell(SpellShape.Random, 2, false), (5, 5), Direction.Down, (10, 10)));
    }
}
