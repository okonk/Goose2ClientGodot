using System;
using Godot;

namespace Goose2Client.UI;

[Flags]
public enum ResizeEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

public static class WindowResize
{
    public static Rect2 Apply(Rect2 start, ResizeEdge edge, Vector2 delta, Vector2 minSize, Vector2 canvas)
    {
        float left = start.Position.X, top = start.Position.Y, right = start.End.X, bottom = start.End.Y;
        if (edge.HasFlag(ResizeEdge.Left))
            left = Mathf.Clamp(left + delta.X, 0f, Mathf.Max(0f, right - minSize.X));
        if (edge.HasFlag(ResizeEdge.Right))
            right = Mathf.Clamp(right + delta.X, Mathf.Min(left + minSize.X, canvas.X), canvas.X);
        if (edge.HasFlag(ResizeEdge.Top))
            top = Mathf.Clamp(top + delta.Y, 0f, Mathf.Max(0f, bottom - minSize.Y));
        if (edge.HasFlag(ResizeEdge.Bottom))
            bottom = Mathf.Clamp(bottom + delta.Y, Mathf.Min(top + minSize.Y, canvas.Y), canvas.Y);
        return new Rect2(left, top, right - left, bottom - top);
    }

    public static Vector2 ScaledSize(Vector2 savedSize, float savedFactor, float factor, Vector2 minSize, Vector2 canvas)
    {
        var size = (savedSize * (factor / (savedFactor > 0f ? savedFactor : 1f))).Round();
        return new Vector2(
            Mathf.Clamp(size.X, minSize.X, Mathf.Max(minSize.X, canvas.X)),
            Mathf.Clamp(size.Y, minSize.Y, Mathf.Max(minSize.Y, canvas.Y)));
    }
}
