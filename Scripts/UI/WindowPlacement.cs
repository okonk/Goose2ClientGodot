using Godot;

namespace Goose2Client.UI;

/// <summary>
/// Placement of a window saved in a saved-canvas design coordinate space, at the current canvas.
/// Pure. Title bar height for y-containment is 24 (Scenes/UI/BaseWindow.tscn:24).
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Canvas of legacy (pre-CanvasSize) settings files and first-run defaults
    /// (<see cref="DefaultWindowLayout"/> is expressed in 1280x720 design coords).
    /// </summary>
    public static readonly Vector2I LegacyCanvas = new(1280, 720);

    public const int TitleBarHeight = 24;

    /// <summary>
    /// Edge-stick + clamp: the window keeps its offset to the NEARER edge in the saved canvas,
    /// re-anchored to that same edge in the current canvas; equidistant (mid-screen) windows
    /// keep their saved coordinate. Identity when savedCanvas == currentCanvas (result is the
    /// saved position clamped into the canvas).
    /// Postcondition: x ∈ [0, currentCanvas.X − w.X]; y ∈ [0, max(0, currentCanvas.Y − 24)]
    /// (title-bar-only y containment — deliberate future-proofing).
    /// </summary>
    public static Vector2 Resolve(Vector2 savedPos, Vector2 windowSize, Vector2I savedCanvas, Vector2I currentCanvas)
    {
        float x = ResolveAxis(savedPos.X, windowSize.X, savedCanvas.X, currentCanvas.X);
        float y = ResolveAxis(savedPos.Y, windowSize.Y, savedCanvas.Y, currentCanvas.Y);
        // Containment; max(0, ·) keeps the result ≥ 0 even for a window larger than the canvas.
        x = Mathf.Clamp(x, 0f, Mathf.Max(0f, currentCanvas.X - windowSize.X));
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, currentCanvas.Y - TitleBarHeight));
        return new Vector2(x, y);
    }

    /// <summary>Per-axis edge-stick in the saved-canvas space, re-anchored to the current canvas.
    /// Offsets: <c>left = saved</c>, <c>right = savedEdge − (saved + size)</c>.</summary>
    private static float ResolveAxis(float saved, float size, int savedEdge, int currentEdge)
    {
        float left = saved;
        float right = savedEdge - (saved + size);
        if (left < right) return left;                 // closer to the leading edge
        if (right < left) return currentEdge - size - right; // re-stick to the trailing edge
        return saved;                                  // equidistant / mid-screen: stays put
    }
}
