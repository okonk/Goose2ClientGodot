using Godot;

namespace Goose2Client.UI;

public static class NoticeColors
{
    public static Color For(NoticeType type) => type switch
    {
        NoticeType.Quest => GameColors.Yellow,
        NoticeType.BuffOn => GameColors.Green,
        NoticeType.BuffOff => new Color(0.7f, 0.7f, 0.7f),
        _ => GameColors.White,
    };
}
