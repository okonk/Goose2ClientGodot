using Godot;

namespace Goose2Client;

public static class MultiWindowMetrics
{
    // Copied from Unity's line prefabs (m_SizeDelta.y = 11.18). Godot's Label renders 13 px
    // rows at size 10 (hhea metrics), so the labels are positioned manually at this pitch.
    private const float LineRowHeight = 11.18f;
    private static readonly Vector2 LinesOrigin = new(6f, 22f);

    // Absolute base position per line (not a scaled per-step pitch: scaling a pitch and
    // multiplying by the index accumulates rounding drift, and the origin would stay unscaled).
    public static Vector2 LinePosition(int index, float factor)
    {
        var basePos = new Vector2(LinesOrigin.X, LinesOrigin.Y + index * LineRowHeight);
        if (factor == 1f)
            return basePos;
        return new Vector2(
            UiScale.ScaleCoordinate(basePos.X, factor),
            UiScale.ScaleCoordinate(basePos.Y, factor));
    }
}
