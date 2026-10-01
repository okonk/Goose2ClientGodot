using Godot;

namespace Goose2Client.UI;

public static class KeyBindingsLayout
{
    public static readonly Vector2 DesignSize = new(760f, 520f);
    public static readonly Vector2 MinSize = new(520f, 320f);
    public const float Margin = 8f;
    public const float TitleBarHeight = 24f;
    public const float RowHeight = 26f;
    public const float ChipHeight = 22f;
    public const float RowPadding = 6f;
    public const float RowSeparation = 8f;
    public const float ChipSeparation = 6f;
    public const float ActionLabelWidth = 160f;
    public const float ChipWidth = 96f;
    public const float RemoveWidth = 16f;
    public const float ResetWidth = 18f;
    public const float HeaderGap = 10f;
}
