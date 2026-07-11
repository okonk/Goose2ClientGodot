using System.Collections.Generic;
using System.Reflection;
using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class GameColorsTests
{
    public static readonly IEnumerable<object[]> UnityColorCases = new[]
    {
        new object[] { nameof(GameColors.Yellow), "f8d000" },
        new object[] { nameof(GameColors.Green),  "88cc40" },
        new object[] { nameof(GameColors.Red),    "fe511c" },
        new object[] { nameof(GameColors.Blue),   "0092ff" },
        new object[] { nameof(GameColors.HpGreen), "70e878" },
        new object[] { nameof(GameColors.HpOrange), "f48532" },
        new object[] { nameof(GameColors.HpRed),  "bf4040" },
    };

    [Theory]
    [MemberData(nameof(UnityColorCases))]
    public void UnityColors_match_expected_hex(string fieldName, string expectedHex)
    {
        var field = typeof(GameColors).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        var color = (Color)field.GetValue(null)!;
        Assert.Equal(expectedHex, color.ToHtml(false));
    }
}
