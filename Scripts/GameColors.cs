using Godot;

namespace Goose2Client;

/// <summary>
/// Shared game palette ported from the Unity client's Colors.cs.
/// Named <c>GameColors</c> (not <c>Colors</c>) to avoid shadowing <see cref="Godot.Colors"/>.
/// 
/// <para>
/// The HP threshold colors (HpGreen, HpOrange, HpRed) originate from the Unity
/// client's health-bar rendering logic and are used for character HP/MP bar coloring.
/// </para>
/// </summary>
public static class GameColors
{
    public static readonly Color White  = new(1f, 1f, 1f);
    public static readonly Color Yellow = Rgb(248, 208, 0);
    public static readonly Color Green  = Rgb(136, 204, 64);
    public static readonly Color Red    = Rgb(254, 81, 28);
    public static readonly Color Blue   = Rgb(0, 146, 255);
    public static readonly Color HpGreen  = Rgb(112, 232, 120);
    public static readonly Color HpOrange = Rgb(244, 133, 50);
    public static readonly Color HpRed    = Rgb(191, 64, 64);

    private static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
}
