using System;
using Godot;

namespace Goose2Client;

public static class GridContainerMetrics
{
    public const int SlotSize = 32;
    public const int Gap = 1;
    public const int Left = 2;
    public const int Top = 20;
    public const int Right = 2;
    public const int Bottom = 4;
    private const int MinWidth = 100;

    public static int Rows(int slots, int columns)
        => Math.Max(1, (int)Math.Ceiling(slots / (double)Math.Max(columns, 1)));

    public static Vector2 SlotPosition(int index, int columns, float factor)
    {
        var cols = Math.Max(columns, 1);
        var col = index % cols;
        var row = index / cols;
        var x = Left + col * (SlotSize + Gap);
        var y = Top + row * (SlotSize + Gap);
        return new Vector2(UiScale.ScaleCoordinate(x, factor), UiScale.ScaleCoordinate(y, factor));
    }

    public static Vector2 WindowSize(int slots, int columns, float factor)
    {
        var cols = Math.Max(columns, 1);
        var rows = Rows(slots, columns);
        var w = Math.Max(MinWidth, Left + cols * SlotSize + (cols - 1) * Gap + Right);
        var h = Top + rows * SlotSize + (rows - 1) * Gap + Bottom;
        return new Vector2(UiScale.ScaleSize(w, factor), UiScale.ScaleSize(h, factor));
    }

    public static (int Slots, int Columns) Sanitize(int slots, int columns)
        => (Math.Clamp(slots, 0, 200), Math.Clamp(columns, 1, 10));
}
