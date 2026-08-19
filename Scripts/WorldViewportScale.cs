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
/// How the world sub-viewport is scaled relative to the root window.
/// </summary>
public enum WorldRenderMode
{
    /// <summary>Integer uniform scale ≥ 2, sub-viewport capped at <see cref="WorldViewportScale.Cap"/>.</summary>
    Integer2x,
    /// <summary>No scaling: sub-viewport and display fill the window 1:1.</summary>
    Native1x,
}

/// <summary>
/// Pure layout math for the capped world sub-viewport: picks an integer uniform display
/// scale so the sub-viewport stays ≤ <see cref="Cap"/>, then computes the centered
/// integer display rectangle. No engine API is used; inputs/outputs are integer values only.
/// </summary>
public static class WorldViewportScale
{
    /// <summary>Maximum sub-viewport resolution in <see cref="WorldRenderMode.Integer2x"/> mode.</summary>
    public static readonly Vector2I Cap = new(1280, 720);

    /// <summary>
    /// Computes the sub-viewport layout for a root window of the given size.
    /// </summary>
    /// <param name="mode">Integer2x (capped, integer scale ≥ 2) or Native1x (1:1 fill).</param>
    /// <param name="windowSize">Root window size in integer pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If either axis of <paramref name="windowSize"/> is &lt; 2.
    /// </exception>
    /// <remarks>
    /// Invariants:
    /// <list type="bullet">
    /// <item><see cref="WorldViewportLayout.DisplaySize"/> == SubViewportSize * Scale exactly (uniform integer scale).</item>
    /// <item>0 ≤ window − DisplaySize &lt; Scale per axis (gutter is sub-scale on each side, and the origin centers it).</item>
    /// <item>Integer2x: Scale ≥ 2 and SubViewportSize ≤ Cap.</item>
    /// </list>
    /// </remarks>
    public static WorldViewportLayout Compute(WorldRenderMode mode, Vector2I windowSize)
    {
        if (windowSize.X < 2 || windowSize.Y < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), $"windowSize must be ≥ (2, 2) per axis, was {windowSize}");
        }

        if (mode == WorldRenderMode.Native1x)
        {
            return new WorldViewportLayout(1, windowSize, new Vector2I(0, 0), windowSize);
        }

        // Integer2x: scale just large enough (and ≥ 2) to fit the window within the cap.
        int scale = 2;
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
}
