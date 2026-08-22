namespace Goose2Client;

public static class TooltipMetrics
{
    public static (int TextColumn, int RightPad, int HeaderTop, int StatsTop, int ExtraBottom,
        int IconSize, int IconOffset, int MinWidth, int NameTop, int TypeTop, int FlagsTop) ItemMetrics(float factor)
        => (
            UiScale.ScaleCoordinate(40f, factor),
            UiScale.ScaleCoordinate(9f, factor),
            UiScale.ScaleCoordinate(46f, factor),
            UiScale.ScaleCoordinate(48f, factor),
            UiScale.ScaleCoordinate(4f, factor),
            UiScale.ScaleSize(32f, factor),
            UiScale.ScaleCoordinate(4f, factor),
            UiScale.ScaleSize(60f, factor),
            UiScale.ScaleCoordinate(2f, factor),
            UiScale.ScaleCoordinate(18f, factor),
            UiScale.ScaleCoordinate(32f, factor));

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
