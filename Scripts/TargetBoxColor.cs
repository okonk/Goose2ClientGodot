using System;
using System.Globalization;
using Godot;

namespace Goose2Client;

/// <summary>The target reticle color, persisted in the character options as "#rrggbb" hex.</summary>
public static class TargetBoxColor
{
    public const string DefaultHex = "#ffffff";

    // A Godot.Color never goes in the settings dictionary: System.Text.Json reflects over its
    // properties, and the native-backed ones fault when no engine is loaded.
    public static string CurrentHex()
    {
        var cs = GameManager.Instance?.CharacterSettings;
        return cs?.GetOption(Options.TargetBoxColor, DefaultHex) ?? DefaultHex;
    }

    public static Color CurrentColor() => Parse(CurrentHex(), out var color) ? color : GameColors.White;

    public static string Format(Color color)
        => "#" + Hex(color.R) + Hex(color.G) + Hex(color.B);

    public static bool Parse(string? hex, out Color color)
    {
        color = GameColors.White;
        if (hex is not { Length: 7 or 9 } || hex[0] != '#')
            return false;
        if (!TryByte(hex, 1, out var r) || !TryByte(hex, 3, out var g) || !TryByte(hex, 5, out var b))
            return false;
        var a = (byte)255;
        if (hex.Length == 9 && !TryByte(hex, 7, out a))
            return false;
        color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        return true;
    }

    private static string Hex(float channel)
        => To255(channel).ToString("x2", CultureInfo.InvariantCulture);

    private static int To255(float channel)
        => Math.Clamp((int)MathF.Round(channel * 255f, MidpointRounding.AwayFromZero), 0, 255);

    private static bool TryByte(string hex, int at, out byte value)
        => byte.TryParse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
