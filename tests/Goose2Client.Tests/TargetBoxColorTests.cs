using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class TargetBoxColorTests
{
    [Fact]
    public void Parse_AcceptsSixDigitHex()
    {
        Assert.True(TargetBoxColor.Parse("#ff8000", out var c));
        Assert.Equal(1f, c.R, 3);
        Assert.Equal(128 / 255f, c.G, 3);
        Assert.Equal(0f, c.B, 3);
        Assert.Equal(1f, c.A);
    }

    [Fact]
    public void Parse_AcceptsUpperCaseAndAlpha()
    {
        Assert.True(TargetBoxColor.Parse("#FF800080", out var c));
        Assert.Equal(1f, c.R, 3);
        Assert.Equal(128 / 255f, c.A, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ff8000")]
    [InlineData("#ff80")]
    [InlineData("#ff800")]
    [InlineData("#ff80000")]
    [InlineData("#gg8000")]
    public void Parse_RejectsMalformed(string? hex)
    {
        Assert.False(TargetBoxColor.Parse(hex, out var c));
        Assert.Equal(GameColors.White, c);
    }

    [Fact]
    public void Format_WritesLowercaseRgbHex()
        => Assert.Equal("#4080bf", TargetBoxColor.Format(new Color(64 / 255f, 128 / 255f, 191 / 255f)));

    [Fact]
    public void Format_ClampsOutOfRangeChannels()
        => Assert.Equal("#00ffff", TargetBoxColor.Format(new Color(-0.5f, 1.4f, 1f)));

    [Fact]
    public void FormatAndParse_RoundTrip()
    {
        var color = new Color(12 / 255f, 200 / 255f, 244 / 255f);
        Assert.True(TargetBoxColor.Parse(TargetBoxColor.Format(color), out var back));
        Assert.Equal(Mathf.RoundToInt(color.R * 255f), Mathf.RoundToInt(back.R * 255f));
        Assert.Equal(Mathf.RoundToInt(color.G * 255f), Mathf.RoundToInt(back.G * 255f));
        Assert.Equal(Mathf.RoundToInt(color.B * 255f), Mathf.RoundToInt(back.B * 255f));
    }

    [Fact]
    public void Option_SurvivesSettingsJsonRoundTrip()
    {
        var settings = new CharacterSettings
        {
            Options = new Dictionary<string, object>
            {
                { Goose2Client.Options.TargetBoxColor, "#4080bf" },
            }
        };

        var back = JsonSerializer.Deserialize<CharacterSettings>(
            JsonSerializer.Serialize(settings, CharacterSettings.JsonOptions), CharacterSettings.JsonOptions)!;

        var stored = back.GetOption(Goose2Client.Options.TargetBoxColor, TargetBoxColor.DefaultHex);
        Assert.Equal("#4080bf", stored);
        Assert.True(TargetBoxColor.Parse(stored, out var color));
        Assert.Equal(64, Mathf.RoundToInt(color.R * 255f));
        Assert.Equal(128, Mathf.RoundToInt(color.G * 255f));
        Assert.Equal(191, Mathf.RoundToInt(color.B * 255f));
    }

    [Fact]
    public void Option_UnsetReadsBackAsWhite()
    {
        var stored = new CharacterSettings().GetOption(
            Goose2Client.Options.TargetBoxColor, TargetBoxColor.DefaultHex);
        Assert.Equal(TargetBoxColor.DefaultHex, stored);
        Assert.True(TargetBoxColor.Parse(stored, out var color));
        Assert.Equal(GameColors.White, color);
    }
}
