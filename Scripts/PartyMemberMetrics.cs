using System;
using Godot;

namespace Goose2Client;

public static class PartyMemberMetrics
{
    public const int FrameWidthPx = 87;
    public const int RowHeightPx = 50;
    public const int EffectIconPx = 16;
    public const int EffectGapPx = 1;

    public static Vector2I MinSize(float factor)
        => new(UiScale.ScaleSize(FrameWidthPx, factor), UiScale.ScaleSize(RowHeightPx, factor));

    public static int EffectRowWidthPx(int count)
        => count * EffectIconPx + Math.Max(0, count - 1) * EffectGapPx;
}
