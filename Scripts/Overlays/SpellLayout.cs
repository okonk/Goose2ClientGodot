namespace Goose2Client.Overlays;

/// <summary>Vertical offset formula ported from Unity SpellAnimation.SetPosition.
/// Returns a negative value (Unity +Y-up sign convention).</summary>
public static class SpellLayout
{
    /// <summary>Computes yOffset = -max((height-48)/2, 0) - 24.
    /// Integer division — faithful Unity port. The returned value is always ≤ 0.</summary>
    public static int VerticalOffsetPixels(int height) => -System.Math.Max((height - 48) / 2, 0) - 24;
}
