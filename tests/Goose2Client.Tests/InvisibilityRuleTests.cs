using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests;

public class InvisibilityRuleTests
{
    [Theory]
    [InlineData(false, false, false, false, InvisibilityState.Normal)]
    [InlineData(false, false, true,  false, InvisibilityState.Normal)]
    [InlineData(false, false, false, true,  InvisibilityState.Normal)]
    [InlineData(false, true,  false, false, InvisibilityState.Normal)]
    [InlineData(true,  false, false, false, InvisibilityState.Hidden)]
    [InlineData(true,  false, true,  false, InvisibilityState.Translucent)]
    [InlineData(true,  false, false, true,  InvisibilityState.Translucent)]
    [InlineData(true,  true,  false, false, InvisibilityState.Translucent)]
    [InlineData(true,  true,  true,  false, InvisibilityState.Translucent)]
    [InlineData(true,  true,  false, true,  InvisibilityState.Translucent)]
    [InlineData(true,  true,  true,  true,  InvisibilityState.Translucent)]
    public void Evaluate_MatchesTruthTable(bool isInvisible, bool canSee, bool isLocal, bool isParty, InvisibilityState expected)
        => Assert.Equal(expected, InvisibilityRule.Evaluate(isInvisible, canSee, isLocal, isParty));
}
