using System;
using Godot;

namespace Goose2Client;

/// <summary>
/// Result of <see cref="WorldViewportScale.Compute"/>: the integer uniform scale,
/// the sub-viewport size, and the display rectangle (origin + size) in root-window
/// integer space.
/// </summary>
/// <param name="Scale">Uniform integer display scale relative to the window.</param>
/// <param name="SubViewportSize">Size of the world sub-viewport (rendered resolution).</param>
/// <param name="DisplayOrigin">Top-left of the centered display rectangle in window space.</param>
/// <param name="DisplaySize">Display rectangle size; always exactly SubViewportSize * Scale.</param>
public readonly record struct WorldViewportLayout(int Scale, Vector2I SubViewportSize, Vector2I DisplayOrigin, Vector2I DisplaySize);

/// <summary>
/// Pure layout math for the capped world sub-viewport: picks an integer uniform display
/// scale so the sub-viewport stays ≤ <see cref="Cap"/>, then computes the centered
/// integer display rectangle. No engine API is used; inputs/outputs are integer values only.
/// </summary>
public static class WorldViewportScale
{
    /// <summary>Maximum sub-viewport resolution in scaled (minScale ≥ 2) modes.</summary>
    public static readonly Vector2I Cap = new(1280, 720);

    /// <summary>Lowest selectable minimum display scale (1 = native 1:1, no scaling).</summary>
    public static readonly int MinScale = 1;

    /// <summary>Highest selectable minimum display scale.</summary>
    public static readonly int MaxScale = 3;

    /// <summary>
    /// Computes the sub-viewport layout for a root window of the given size.
    /// </summary>
    /// <param name="minScale">Minimum integer display scale in [MinScale, MaxScale]; 1 = native 1:1 fill, ≥ 2 = capped integer scale.</param>
    /// <param name="windowSize">Root window size in integer pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If <paramref name="minScale"/> is outside [MinScale, MaxScale] or either axis of
    /// <paramref name="windowSize"/> is &lt; 2.
    /// </exception>
    /// <remarks>
    /// Invariants:
    /// <list type="bullet">
    /// <item><see cref="WorldViewportLayout.DisplaySize"/> == SubViewportSize * Scale exactly (uniform integer scale).</item>
    /// <item>0 ≤ window − DisplaySize &lt; Scale per axis (gutter is sub-scale on each side, and the origin centers it).</item>
    /// <item>minScale ≥ 2: Scale ≥ minScale and SubViewportSize ≤ Cap.</item>
    /// </list>
    /// </remarks>
    public static WorldViewportLayout Compute(int minScale, Vector2I windowSize)
    {
        if (minScale < MinScale || minScale > MaxScale)
        {
            throw new ArgumentOutOfRangeException(nameof(minScale), $"minScale must be in [{MinScale}, {MaxScale}], was {minScale}");
        }
        if (windowSize.X < 2 || windowSize.Y < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), $"windowSize must be ≥ (2, 2) per axis, was {windowSize}");
        }

        if (minScale == MinScale)
        {
            return new WorldViewportLayout(1, windowSize, new Vector2I(0, 0), windowSize);
        }

        // Scale just large enough (and ≥ minScale) to fit the window within the cap.
        int scale = minScale;
        if (windowSize.X > Cap.X)
        {
            scale = Math.Max(scale, (windowSize.X + Cap.X - 1) / Cap.X);
        }
        if (windowSize.Y > Cap.Y)
        {
            scale = Math.Max(scale, (windowSize.Y + Cap.Y - 1) / Cap.Y);
        }

        var subViewportSize = new Vector2I(windowSize.X / scale, windowSize.Y / scale); // floor
        var displaySize = new Vector2I(subViewportSize.X * scale, subViewportSize.Y * scale);
        var displayOrigin = new Vector2I((windowSize.X - displaySize.X) / 2, (windowSize.Y - displaySize.Y) / 2); // truncated, remainder < scale so remainder/2 < scale/2
        return new WorldViewportLayout(scale, subViewportSize, displayOrigin, displaySize);
    }

    /// <summary>
    /// True if <paramref name="windowPos"/> (root-window integer space) is inside the display
    /// rectangle: origin inclusive, origin + DisplaySize exclusive on both axes.
    /// Gutter clicks must use this to reject.
    /// </summary>
    public static bool IsInsideDisplay(WorldViewportLayout layout, Vector2I windowPos)
    {
        Vector2I end = layout.DisplayOrigin + layout.DisplaySize;
        return windowPos.X >= layout.DisplayOrigin.X
            && windowPos.X < end.X
            && windowPos.Y >= layout.DisplayOrigin.Y
            && windowPos.Y < end.Y;
    }

    /// <summary>
    /// Camera2D.Offset that cancels the viewport-center parity: the camera anchors to the
    /// viewport center, a half pixel on odd axes, so +0.5 there makes the canvas translation
    /// integral for an integral camera position (a moving camera is fractional regardless).
    /// </summary>
    public static Vector2 CameraParityOffset(Vector2I subViewportSize)
        => new((subViewportSize.X % 2) * 0.5f, (subViewportSize.Y % 2) * 0.5f);
}
