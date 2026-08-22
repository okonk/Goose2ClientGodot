using Godot;

namespace Goose2Client;

public static class VitalsPortraitMetrics
{
    private const float Zoom = 1.25f;
    private const float CircleSize = 53f;

    public static (Vector2 RectSize, Vector2 RectPosition) Layout(Vector2 texSize, float dropPx, float factor)
    {
        var size = texSize * Zoom * factor;
        var center = CircleSize * factor / 2f;
        return (size, new Vector2(center - size.X / 2f, center + dropPx * factor - size.Y / 2f));
    }
}
