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

    // Windows closer than ¼ of the canvas to either edge are edge-parked; the band is the middle 50%.
    private const float MiddleBandEdgeFraction = 0.25f;

    /// <summary>
    /// Middle-band + edge-stick + clamp at the 1× baseline (legacy pre-quad path: saved size is
    /// the live size, factors 1). Delegates to <see cref="ResolveScaled"/>; mathematically identical
    /// to the old direct form for every input.
    /// Postcondition: x ∈ [0, currentCanvas.X − w.X]; y ∈ [0, max(0, currentCanvas.Y − 24)]
    /// (title-bar-only y containment — deliberate future-proofing).
    /// </summary>
    public static Vector2 Resolve(Vector2 savedPos, Vector2 windowSize, Vector2I savedCanvas, Vector2I currentCanvas)
        => ResolveScaled(savedPos, windowSize, 1f, savedCanvas, windowSize, 1f, currentCanvas, TitleBarHeight);

    /// <summary>
    /// Saved-quad placement: all saved-space quantities derive from savedSize; edge margins are
    /// logical px scaled by factor/savedFactor (≤ 0 → 1); no rounding — exact identity at marginScale 1.
    /// </summary>
    public static Vector2 ResolveScaled(Vector2 savedPos, Vector2 savedSize, float savedFactor, Vector2I savedCanvas,
        Vector2 windowSize, float factor, Vector2I currentCanvas, int titleBarAllowance = TitleBarHeight)
    {
        float marginScale = factor / (savedFactor > 0f ? savedFactor : 1f);
        float x = ResolveAxisScaled(savedPos.X, savedSize.X, windowSize.X, marginScale, savedCanvas.X, currentCanvas.X);
        float y = ResolveAxisScaled(savedPos.Y, savedSize.Y, windowSize.Y, marginScale, savedCanvas.Y, currentCanvas.Y);
        // Containment; max(0, ·) keeps the result ≥ 0 even for a window larger than the canvas.
        x = Mathf.Clamp(x, 0f, Mathf.Max(0f, currentCanvas.X - windowSize.X));
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, currentCanvas.Y - titleBarAllowance));
        return new Vector2(x, y);
    }

    /// <summary>Per-axis edge-stick in the saved-canvas space, re-anchored to the current canvas.
    /// Offsets: <c>left = saved</c>, <c>right = savedEdge − (saved + savedSize)</c>.</summary>
    private static float ResolveAxisScaled(float saved, float savedSize, float size, float marginScale, int savedEdge, int currentEdge)
    {
        float left = saved;
        float right = savedEdge - (saved + savedSize);
        // Middle-band: ≥ MiddleBandEdgeFraction of the saved canvas from both edges → parked in
        // the middle, keep the saved coordinate unscaled. Known limitation: band-keep does NOT
        // survive clamping onto a smaller canvas; a window dragged re-centers it.
        if (left >= MiddleBandEdgeFraction * savedEdge && right >= MiddleBandEdgeFraction * savedEdge) return saved;
        if (left < right) return left * marginScale;                 // closer to the leading edge
        if (right < left) return currentEdge - size - right * marginScale; // re-stick to the trailing edge
        // Equidistant: stays put. The band above intercepts this unless the window is wider
        // than half the saved canvas, where it still fires.
        return saved;
    }

    /// <summary>
    /// First-run position for transient dialog windows: centered in <c>canvas</c>.
    /// Result = (canvas − windowSize) / 2 truncated per axis, clamped to ≥ 0
    /// (a window larger than the canvas lands at (0,0)).
    /// </summary>
    public static Vector2 Center(Vector2I canvas, Vector2 windowSize)
    {
        float x = Mathf.Max(0f, (int)((canvas.X - windowSize.X) / 2f));
        float y = Mathf.Max(0f, (int)((canvas.Y - windowSize.Y) / 2f));
        return new Vector2(x, y);
    }
}
