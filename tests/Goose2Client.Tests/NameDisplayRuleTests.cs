using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests;

public class NameDisplayRuleTests
{
    [Theory]
    [InlineData(NameDisplayMode.Always, CharacterType.Player, false, true)]
    [InlineData(NameDisplayMode.Always, CharacterType.Monster, false, true)]
    [InlineData(NameDisplayMode.Always, CharacterType.Pet, false, true)]
    [InlineData(NameDisplayMode.PlayersOnly, CharacterType.Player, false, true)]
    [InlineData(NameDisplayMode.PlayersOnly, CharacterType.Monster, false, false)]
    [InlineData(NameDisplayMode.PlayersOnly, CharacterType.Vendor, false, false)]
    [InlineData(NameDisplayMode.PlayersOnly, CharacterType.Pet, false, false)]
    [InlineData(NameDisplayMode.Never, CharacterType.Player, false, false)]
    [InlineData(NameDisplayMode.Never, CharacterType.Monster, false, false)]
    [InlineData(NameDisplayMode.Always, CharacterType.Player, true, false)]      // adversarial: server override beats Always
    [InlineData(NameDisplayMode.PlayersOnly, CharacterType.Player, true, false)]
    public void ShouldRenderNameOverhead_MatchesTruthTable(NameDisplayMode mode, CharacterType type, bool serverHidden, bool expected)
        => Assert.Equal(expected, NameDisplayRule.ShouldRenderNameOverhead(mode, type, serverHidden));

    [Theory]
    [InlineData(true, false, false, true)]    // visible, not roof, not overhead -> tooltip
    [InlineData(true, false, true, false)]    // overhead shown -> no tooltip
    [InlineData(true, true, false, false)]    // roof-occluded -> no tooltip
    [InlineData(false, false, false, false)]  // hidden from viewer -> no tooltip
    public void ShouldShowNameTooltip_MatchesTruthTable(bool visibleToViewer, bool roofOccluded, bool overheadShown, bool expected)
        => Assert.Equal(expected, NameDisplayRule.ShouldShowNameTooltip(visibleToViewer, roofOccluded, overheadShown));
}
