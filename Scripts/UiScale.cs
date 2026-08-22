using System;
using Godot;

namespace Goose2Client;

public enum UiScaleMode
{
    Auto = 0,
    Manual = 1
}

public class UiScale
{
    public const float MinFactor = 1f;
    public const float MaxFactor = 3f;
    public const float Step = 0.5f;

    public float CurrentFactor { get; set; }

    public static float NormalizeFactor(float raw)
    {
        if (float.IsNaN(raw))
        {
            raw = MinFactor;
        }

        float snapped = MathF.Round(raw / Step, MidpointRounding.AwayFromZero) * Step;
        return Math.Clamp(snapped, MinFactor, MaxFactor);
    }

    public static int AutoFactor(int windowHeightPx)
        => windowHeightPx < 1080 ? 1 : windowHeightPx < 2160 ? 2 : 3;

    public static UiScaleMode NormalizeMode(int raw)
        => raw == 1 ? UiScaleMode.Manual : UiScaleMode.Auto;

    public static float Resolve(UiScaleMode mode, float savedValue, int windowHeightPx)
    {
        if (mode != UiScaleMode.Manual)
            return AutoFactor(windowHeightPx);
        return NormalizeFactor(savedValue);
    }

    public static int ScaleCoordinate(float value, float factor)
        => (int)MathF.Round(value * factor, MidpointRounding.AwayFromZero);

    public static int ScaleSize(float basePx, float factor)
        => Math.Max(1, ScaleCoordinate(basePx, factor));

    public int ScaleSize(float basePx)
        => ScaleSize(basePx, CurrentFactor);

    public Vector2I ScaleSizeI(Vector2I v)
        => new(ScaleSize(v.X), ScaleSize(v.Y));
}
