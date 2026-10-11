using Goose2Client.Overlays;
using Xunit;
using BattleTextType = Goose2Client.Network.Packets.BattleTextType;

public class BattleTextMergeRuleTests
{
    [Theory]
    [InlineData(BattleTextType.Red1, "-123", -123L, "")]
    [InlineData(BattleTextType.Red2, "-123", -123L, "")]
    [InlineData(BattleTextType.Red61, "-1", -1L, "")]
    [InlineData(BattleTextType.Green7, "+456", 456L, "")]
    [InlineData(BattleTextType.Green8, "+456", 456L, "")]
    [InlineData(BattleTextType.Yellow60, "+1 234 XP", 1234L, "XP")]
    [InlineData(BattleTextType.Yellow60, "+1,234 XP", 1234L, "XP")]
    [InlineData(BattleTextType.Yellow60, "+1 234 567 XP", 1234567L, "XP")]
    [InlineData(BattleTextType.Yellow60, "1234 567", 1234L, "567")]
    [InlineData(BattleTextType.Yellow60, "123", 123L, "")]
    public void TryGet_ParsesValueAndSuffix(BattleTextType type, string text, long value, string suffix)
    {
        Assert.True(BattleTextMergeRule.TryGet(type, text, out var key, out var parsed));
        Assert.Equal(value, parsed);
        Assert.Equal(suffix, key.Suffix);
    }

    [Fact]
    public void Keys_SameFamilySameSuffix_ShareAcrossTypes()
    {
        BattleTextMergeRule.TryGet(BattleTextType.Red1, "-123", out var a, out _);
        BattleTextMergeRule.TryGet(BattleTextType.Red5, "-123", out var b, out _);
        BattleTextMergeRule.TryGet(BattleTextType.Green7, "+5", out var c, out _);
        BattleTextMergeRule.TryGet(BattleTextType.Green8, "+5", out var d, out _);
        Assert.Equal(a, b);
        Assert.Equal(c, d);
        Assert.NotEqual(a, c);
    }

    [Theory]
    [InlineData(BattleTextType.Yellow60, "Level Up!")]
    [InlineData(BattleTextType.Yellow60, "Fizzle")]
    [InlineData(BattleTextType.Yellow60, "+1,2,3")]
    [InlineData(BattleTextType.Yellow60, "+1,23")]
    [InlineData(BattleTextType.Yellow60, "1234,567")]
    [InlineData(BattleTextType.Yellow60, "12KB")]
    [InlineData(BattleTextType.Yellow60, "5s ")]
    [InlineData(BattleTextType.Yellow60, "1m 5s ")]
    [InlineData(BattleTextType.Yellow60, "1.5s ")]
    [InlineData(BattleTextType.Yellow60, "500ms")]
    [InlineData(BattleTextType.Yellow60, "")]
    [InlineData(BattleTextType.Stunned50, "")]
    [InlineData(BattleTextType.Miss21, "21")]
    [InlineData(BattleTextType.Dodge20, "20")]
    [InlineData(BattleTextType.White, "5")]
    public void TryGet_PassThrough(BattleTextType type, string text)
        => Assert.False(BattleTextMergeRule.TryGet(type, text, out _, out _));

    [Fact]
    public void TryGet_XpAndBareYellow_GetDifferentKeys()
    {
        BattleTextMergeRule.TryGet(BattleTextType.Yellow60, "+1 234 XP", out var xp, out _);
        BattleTextMergeRule.TryGet(BattleTextType.Yellow60, "+5", out var bare, out _);
        Assert.NotEqual(xp, bare);
    }

    [Theory]
    [InlineData(-85L, "", "-85")]
    [InlineData(-1234L, "", "-1,234")]
    [InlineData(120L, "", "+120")]
    [InlineData(1234567L, "XP", "+1,234,567 XP")]
    [InlineData(0L, "", "+0")]
    public void Format_N0Signed(long total, string suffix, string expected)
    {
        var key = new BattleTextMergeKey(BattleTextFamily.Red, suffix);
        Assert.Equal(expected, BattleTextMergeRule.Format(key, total));
    }
}
