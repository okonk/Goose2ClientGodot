using Godot;

namespace Goose2Client.UI;

public static class LogViewerLayout
{
    public static readonly Vector2 DesignSize = new(1000f, 620f);
    public static readonly Vector2 MinSize = new(620f, 340f);
    public const float Margin = 8f;
    public const float TitleBarHeight = 24f;
    public const float FreshnessRowHeight = 14f;
    public const float FilterRowHeight = 24f;
    public const float ActionRowHeight = 26f;
    public const float RowGap = 6f;
    public const float SuggestionMaxHeight = 96f;
    public const float ResultsRatio = 0.58f;
    public const int ResultsStretchRatio = 58;
    public const int DetailsStretchRatio = 42;

    public static float FilterAreaHeight => Margin + FreshnessRowHeight + FilterRowHeight * 2 + ActionRowHeight + RowGap * 4;

    public static readonly string[] ColumnHeaders = { "UTC", "Event type", "Primary", "Related", "Map", "Summary" };

    public static readonly float[] ColumnMinimums = { 150f, 95f, 70f, 70f, 85f, 90f };
    public static readonly float[] ColumnExpandRatios = { 0f, 1.0f, 0.5f, 0.5f, 1.0f, 4.0f };

    public static float ResultsWidth(Vector2 canvas) => (canvas.X - 2f * Margin) * ResultsRatio;

    public static float DetailsWidth(Vector2 canvas) => (canvas.X - 2f * Margin) - ResultsWidth(canvas);

    public static (float Results, float Details) SplitPanels(Vector2 canvas)
        => (ResultsWidth(canvas), DetailsWidth(canvas));

    public static float[] ColumnWidths(float available)
    {
        float sumMin = 0f;
        foreach (float m in ColumnMinimums)
            sumMin += m;
        var widths = (float[])ColumnMinimums.Clone();
        if (available <= sumMin)
            return widths;
        float extra = available - sumMin;
        float sumRatios = 0f;
        foreach (float r in ColumnExpandRatios)
            sumRatios += r;
        for (int i = 0; i < widths.Length; i++)
            widths[i] = (float)((double)ColumnMinimums[i] + (double)ColumnExpandRatios[i] / sumRatios * extra);
        return widths;
    }
}
