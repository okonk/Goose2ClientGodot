using Godot;

namespace Goose2Client.UI;

public static class KeyBindingsLayout
{
    public static readonly Vector2 DesignSize = new(760f, 520f);
    public static readonly Vector2 MinSize = new(520f, 320f);
    public const float Margin = 8f;
    public const float TitleBarHeight = 24f;
    public const float RowHeight = 24f;
    public const float RowSeparation = 4f;
    public const float ChipSeparation = 2f;
    public const float ActionLabelWidth = 140f;
    public const float ChipWidth = 88f;
    public const float RemoveWidth = 24f;
    public const float ButtonWidth = 88f;
}
