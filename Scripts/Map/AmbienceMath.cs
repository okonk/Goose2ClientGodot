using Godot;

namespace Goose2Client.Map;

public static class AmbienceMath
{
    public const float LocalLightIntensity = 0.6f;

    public static float TimeOfDay(long elapsedMs, long durationMs)
        => durationMs <= 0 ? 0.5f : (elapsedMs % durationMs) / (float)durationMs;

    public static float Darkness(float startAlpha, float endAlpha, long elapsedMs, long durationMs)
    {
        if (durationMs <= 0) return startAlpha;
        var t = (elapsedMs % durationMs) / (float)durationMs;
        return Mathf.Lerp(startAlpha, endAlpha, 1f - Mathf.Sin(t * Mathf.Pi));
    }

    public static Color ModulateColor(uint tintRgba, float darkness)
    {
        // tintRgba is packed #rrggbbaa; the alpha byte is ignored — darkness drives it.
        var r = ((tintRgba >> 24) & 0xFF) / 255f;
        var g = ((tintRgba >> 16) & 0xFF) / 255f;
        var b = ((tintRgba >> 8) & 0xFF) / 255f;
        return new Color(1f - darkness + r * darkness, 1f - darkness + g * darkness, 1f - darkness + b * darkness, 1f);
    }

    public static bool IsNight(float timeOfDay)
        => timeOfDay < 0.25f || timeOfDay > 0.75f;
}
