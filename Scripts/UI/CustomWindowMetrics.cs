using System;
using Godot;

namespace Goose2Client.UI;

public static class CustomWindowMetrics
{
    public const int DefaultR = 255;
    public const int DefaultG = 255;
    public const int DefaultB = 255;
    public const int DefaultA = 160;

    // Corners: red (top-left), green (top-right), blue (bottom-left), white (bottom-right).
    public static Vector3I SampleGradient(Vector2 pos)
    {
        float u = Mathf.Clamp(pos.X, 0f, 1f);
        float v = Mathf.Clamp(pos.Y, 0f, 1f);
        float r = (1f - v) * (1f - u) + v * u;
        float g = u;
        float b = v;
        return new Vector3I(
            (int)MathF.Round(r * 255f),
            (int)MathF.Round(g * 255f),
            (int)MathF.Round(b * 255f));
    }
}
