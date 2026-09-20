using Godot;

namespace Goose2Client;

public static class OptionListMetrics
{
    public const int IconSize = 32;
    public const int IconTextGap = 4;
    public const int IconX = 0;
    private const int LinePaddingY = 2;
    private const int LineHeight = IconSize + LinePaddingY * 2;
    private const int HeadingHeight = 18;
    private const float LinesOriginX = 6f;
    private const float LinesOriginY = 22f;
    public const float LinesWidth = 248f;
    private const float BottomMargin = 6f;
    private const float ButtonRowHeight = 26f;
    private const float ButtonWidth = 74f;

    // Absolute base position per line (not a scaled per-step pitch: scaling a pitch and
    // multiplying by the index accumulates rounding drift, and the origin would stay unscaled).
    // When a heading is shown the whole block of lines is pushed down by one heading row.
    public static Vector2 LinePosition(int index, float factor, bool hasHeading)
    {
        var originY = LinesOriginY + (hasHeading ? HeadingHeight : 0f);
        var basePos = new Vector2(LinesOriginX, originY + index * LineHeight);
        if (factor == 1f)
            return basePos;
        return new Vector2(
            UiScale.ScaleCoordinate(basePos.X, factor),
            UiScale.ScaleCoordinate(basePos.Y, factor));
    }

    public static Vector2 LineSize(float factor)
        => new(UiScale.ScaleSize(LinesWidth, factor), UiScale.ScaleSize(LineHeight, factor));

    public static Vector2 HeadingPosition(float factor)
        => new(UiScale.ScaleCoordinate(LinesOriginX, factor), UiScale.ScaleCoordinate(LinesOriginY, factor));

    public static Vector2 HeadingSize(float factor)
        => new(UiScale.ScaleSize(LinesWidth, factor), UiScale.ScaleSize(HeadingHeight, factor));

    // Leading indent for a line's text so it clears the 32px icon (applied to every line so the
    // text column stays aligned whether or not a given line has an icon).
    public static int LineTextIndent(float factor)
        => (int)UiScale.ScaleSize(IconSize + IconTextGap, factor);

    public static int LineTextWidth(float factor)
        => (int)UiScale.ScaleSize(LinesWidth - (IconSize + IconTextGap), factor);

    public static int WindowHeight(int lineCount, float factor, bool hasHeading, bool bottomButtons)
    {
        var h = LinesOriginY + (hasHeading ? HeadingHeight : 0f) + lineCount * LineHeight + BottomMargin;
        if (bottomButtons)
            h += ButtonRowHeight;
        return UiScale.ScaleSize(h, factor);
    }

    public static Vector2 BottomButtonSize(float factor)
        => new(UiScale.ScaleSize(ButtonWidth, factor), UiScale.ScaleSize(ButtonRowHeight, factor));

    public static float BottomButtonY(float windowHeight, float factor)
        => windowHeight - UiScale.ScaleSize(BottomMargin + ButtonRowHeight, factor);
}
