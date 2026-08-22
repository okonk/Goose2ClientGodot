namespace Goose2Client;

public static class TooltipMetrics
{
    public static (int TextColumn, int RightPad, int HeaderTop, int StatsTop, int ExtraBottom,
        int IconSize, int IconOffset, int MinWidth, int NameTop, int TypeTop, int FlagsTop) ItemMetrics(float factor)
        => (
            UiScale.ScaleCoordinate(52f, factor),
            UiScale.ScaleCoordinate(10f, factor),
            UiScale.ScaleCoordinate(49f, factor),
            UiScale.ScaleCoordinate(51f, factor),
            UiScale.ScaleCoordinate(10f, factor),
            UiScale.ScaleSize(32f, factor),
            UiScale.ScaleCoordinate(10f, factor),
            UiScale.ScaleSize(60f, factor),
            UiScale.ScaleCoordinate(10f, factor),
            UiScale.ScaleCoordinate(23f, factor),
            UiScale.ScaleCoordinate(36f, factor));

    public static (int W, int H) TextPad(float factor)
        => (UiScale.ScaleSize(8f, factor), UiScale.ScaleSize(4f, factor));

    public static (int LeftMargin, int TopMargin, int RowGap, int BottomMargin, int LabelWidth) MapItemMetrics(float factor)
        => (
            UiScale.ScaleCoordinate(6f, factor),
            UiScale.ScaleCoordinate(4f, factor),
            UiScale.ScaleCoordinate(2f, factor),
            UiScale.ScaleCoordinate(4f, factor),
            UiScale.ScaleSize(400f, factor));
}
