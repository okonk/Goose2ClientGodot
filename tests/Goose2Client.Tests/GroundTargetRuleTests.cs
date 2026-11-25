using Goose2Client;
using Xunit;

namespace Goose2Client.Tests;

public class GroundTargetRuleTests
{
    private static SpellInfo Spell(bool ground) => new() { Name = "Fire Patch", GroundTargetable = ground };

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(30, 10, true)]
    [InlineData(31, 10, false)]
    [InlineData(10, 18, true)]
    [InlineData(10, 19, false)]
    [InlineData(-1, 10, false)]
    [InlineData(10, 100, false)]
    public void Accepts_only_tiles_on_the_map_and_in_view(int x, int y, bool ok)
    {
        Assert.Equal(ok, GroundTargetRule.CanCastAt(Spell(true), player: (10, 10), tile: (x, y),
            viewRange: (20, 8), mapSize: (100, 100)));
    }

    [Fact]
    public void Refuses_spells_that_are_not_ground_targetable()
    {
        Assert.False(GroundTargetRule.CanCastAt(Spell(false), (10, 10), (11, 10), (20, 8), (100, 100)));
    }
}
