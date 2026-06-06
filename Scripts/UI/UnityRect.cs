namespace Goose2Client.UI;

/// <summary>A Godot Control rect in top-left/y-down pixel coordinates.</summary>
public readonly struct GodotRect
{
    public readonly float Left, Top, Width, Height;
    public GodotRect(float left, float top, float width, float height)
    { Left = left; Top = top; Width = width; Height = height; }
}

/// <summary>
/// Converts a Unity RectTransform (y-up, center-origin anchors/pivot) into Godot
/// Control offsets (y-down, top-left). Assumes a point anchor (anchorMin == anchorMax).
/// </summary>
public static class UnityRect
{
    public static GodotRect ToGodot(
        float parentW, float parentH,
        float anchorX, float anchorY,
        float pivotX, float pivotY,
        float anchoredX, float anchoredY,
        float w, float h)
    {
        float left = anchorX * parentW + anchoredX - pivotX * w;
        float top  = parentH - anchorY * parentH - anchoredY - (1f - pivotY) * h;
        return new GodotRect(left, top, w, h);
    }
}
