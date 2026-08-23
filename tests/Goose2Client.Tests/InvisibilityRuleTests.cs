using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests;

public class InvisibilityRuleTests
{
    [Theory]
    [InlineData(false, false, false, InvisibilityState.Normal)]
    [InlineData(false, false, true,  InvisibilityState.Normal)]
    [InlineData(false, true,  false, InvisibilityState.Normal)]
    [InlineData(false, true,  true,  InvisibilityState.Normal)]
    [InlineData(true,  false, false, InvisibilityState.Hidden)]
    [InlineData(true,  false, true,  InvisibilityState.Translucent)]
    [InlineData(true,  true,  false, InvisibilityState.Translucent)]
    [InlineData(true,  true,  true,  InvisibilityState.Translucent)]
    public void Evaluate_MatchesTruthTable(bool isInvisible, bool canSee, bool isLocal, InvisibilityState expected)
        => Assert.Equal(expected, InvisibilityRule.Evaluate(isInvisible, canSee, isLocal));
}
