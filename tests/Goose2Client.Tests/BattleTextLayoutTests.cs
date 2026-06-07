using Godot;
using Goose2Client.Overlays;
using Xunit;

using BattleTextType = Goose2Client.Network.Packets.BattleTextType;

public class BattleTextLayoutTests
{
    // ── Spread offset cycle (spread types: Red1, Red2, Red4, Red5, Green7, Green8) ──

    [Fact]
    public void Spread_FirstCall_childCount0_returnsOffset4_0_positionStays0()
    {
        int position = 0;
        var offset = BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 0, ref position);
        Assert.Equal(new Vector2(4, 0), offset);
        Assert.Equal(0, position);
    }

    [Fact]
    public void Spread_SecondCall_childCount1_returnsOffsetNeg4_0_positionBecomes1()
    {
        int position = 0;
        BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 0, ref position); // call 1
        var offset = BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 1, ref position);
        Assert.Equal(new Vector2(-4, 0), offset);
        Assert.Equal(1, position);
    }

    [Fact]
    public void Spread_ThirdCall_childCount2_returnsOffset12_0_positionBecomes2()
    {
        int position = 0;
        BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 0, ref position);
        BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 1, ref position);
        var offset = BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 2, ref position);
        Assert.Equal(new Vector2(12, 0), offset);
        Assert.Equal(2, position);
    }

    [Fact]
    public void Spread_FourthCall_childCount3_returnsOffset4_neg8_positionBecomes3()
    {
        int position = 0;
        BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 0, ref position);
        BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 1, ref position);
        BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 2, ref position);
        var offset = BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 3, ref position);
        Assert.Equal(new Vector2(4, -8), offset);
        Assert.Equal(3, position);
    }

    [Fact]
    public void Spread_FullCycle9Calls_producesCorrectSequence()
    {
        // Expected offsets (Godot sign: y negated from Unity):
        // call  childCount  position  x    y_unity  y_godot
        //  1       0          0       4     0        0
        //  2       1          1      -4     0        0
        //  3       2          2      12     0        0
        //  4       3          3       4     8       -8
        //  5       4          4      -4     8       -8
        //  6       5          5      12     8       -8
        //  7       6          6       4    16      -16
        //  8       7          7      -4    16      -16
        //  9       8          8      12    16      -16
        var expected = new Vector2[]
        {
            new(4, 0), new(-4, 0), new(12, 0),
            new(4, -8), new(-4, -8), new(12, -8),
            new(4, -16), new(-4, -16), new(12, -16),
        };

        int position = 0;
        for (int i = 0; i < 9; i++)
        {
            var offset = BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, i, ref position);
            Assert.Equal(expected[i], offset);
        }
    }

    [Fact]
    public void Spread_PositionWrapsAfter9()
    {
        int position = 0;
        for (int i = 0; i < 9; i++)
            BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, i, ref position);
        // 10th call: childCount=9, position wraps to 0
        var offset = BattleTextLayout.ComputeSpreadOffset(BattleTextType.Red1, 9, ref position);
        Assert.Equal(new Vector2(4, -16), offset);  // y capped at 16 (min(9/3,2)*8 = min(3,2)*8 = 16)
        Assert.Equal(0, position);
    }

    [Fact]
    public void Spread_AllSpreadTypesWork()
    {
        var spreadTypes = new[]
        {
            BattleTextType.Red1, BattleTextType.Red2, BattleTextType.Red4,
            BattleTextType.Red5, BattleTextType.Green7, BattleTextType.Green8,
        };
        foreach (var t in spreadTypes)
        {
            int position = 0;
            var offset = BattleTextLayout.ComputeSpreadOffset(t, 0, ref position);
            Assert.Equal(new Vector2(4, 0), offset);
        }
    }

    [Fact]
    public void NonSpreadTypes_returnZeroOffset_andDoNotChangePosition()
    {
        var nonSpreadTypes = new[]
        {
            BattleTextType.White, BattleTextType.Stunned10, BattleTextType.Rooted11,
            BattleTextType.Dodge20, BattleTextType.Miss21, BattleTextType.Stunned50,
            BattleTextType.Rooted51, BattleTextType.Yellow60, BattleTextType.Red61,
        };
        foreach (var t in nonSpreadTypes)
        {
            int position = 42;
            var offset = BattleTextLayout.ComputeSpreadOffset(t, 5, ref position);
            Assert.Equal(new Vector2(0, 0), offset);
            Assert.Equal(42, position);  // unchanged
        }
    }

    // ── Color + display-text resolution ──

    [Fact]
    public void Resolve_Red1_returnsRed154_0_0_andUnchangedText()
    { AssertResolve(BattleTextType.Red1, new Color(154f/255f, 0f, 0f, 1f), "text"); }

    [Fact]
    public void Resolve_Red2_returnsRed154_0_0_andUnchangedText()
    { AssertResolve(BattleTextType.Red2, new Color(154f/255f, 0f, 0f, 1f), "text"); }

    [Fact]
    public void Resolve_Red4_returnsRed154_0_0_andUnchangedText()
    { AssertResolve(BattleTextType.Red4, new Color(154f/255f, 0f, 0f, 1f), "text"); }

    [Fact]
    public void Resolve_Red5_returnsRed154_0_0_andUnchangedText()
    { AssertResolve(BattleTextType.Red5, new Color(154f/255f, 0f, 0f, 1f), "text"); }

    [Fact]
    public void Resolve_Red61_returnsRed154_0_0_andUnchangedText()
    { AssertResolve(BattleTextType.Red61, new Color(154f/255f, 0f, 0f, 1f), "text"); }

    [Fact]
    public void Resolve_Green7_returnsGreen136_204_64_andUnchangedText()
    { AssertResolve(BattleTextType.Green7, new Color(136f/255f, 204f/255f, 64f/255f, 1f), "text"); }

    [Fact]
    public void Resolve_Green8_returnsGreen136_204_64_andUnchangedText()
    { AssertResolve(BattleTextType.Green8, new Color(136f/255f, 204f/255f, 64f/255f, 1f), "text"); }

    [Fact]
    public void Resolve_Yellow60_returnsYellow248_208_0_andUnchangedText()
    { AssertResolve(BattleTextType.Yellow60, new Color(248f/255f, 208f/255f, 0f/255f, 1f), "text"); }

    [Fact]
    public void Resolve_Stunned10_returnsWhite_andSTUNNED()
    { AssertResolve(BattleTextType.Stunned10, Colors.White, "STUNNED"); }

    [Fact]
    public void Resolve_Stunned50_returnsWhite_andSTUNNED()
    { AssertResolve(BattleTextType.Stunned50, Colors.White, "STUNNED"); }

    [Fact]
    public void Resolve_Rooted11_returnsWhite_andROOTED()
    { AssertResolve(BattleTextType.Rooted11, Colors.White, "ROOTED"); }

    [Fact]
    public void Resolve_Rooted51_returnsWhite_andROOTED()
    { AssertResolve(BattleTextType.Rooted51, Colors.White, "ROOTED"); }

    [Fact]
    public void Resolve_Dodge20_returnsWhite_andDODGE()
    { AssertResolve(BattleTextType.Dodge20, Colors.White, "DODGE"); }

    [Fact]
    public void Resolve_Miss21_returnsWhite_andMISS()
    { AssertResolve(BattleTextType.Miss21, Colors.White, "MISS"); }

    [Fact]
    public void Resolve_White_returnsWhite_andUnchangedText()
    { AssertResolve(BattleTextType.White, Colors.White, "text"); }

    private static void AssertResolve(BattleTextType type, Color expectedColor, string expectedText)
    {
        var (color, text) = BattleTextLayout.Resolve(type, "text");
        Assert.Equal(expectedColor, color);
        Assert.Equal(expectedText, text);
    }
}
