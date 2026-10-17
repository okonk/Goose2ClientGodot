using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.UI.Tests;

public class NoticeColorsTests
{
    [Fact]
    public void For_InfoIsWhite()
    {
        Assert.Equal(GameColors.White, NoticeColors.For(NoticeType.Info));
    }

    [Fact]
    public void For_QuestIsYellow()
    {
        Assert.Equal(GameColors.Yellow, NoticeColors.For(NoticeType.Quest));
    }

    [Fact]
    public void For_BuffOnIsGreen()
    {
        Assert.Equal(GameColors.Green, NoticeColors.For(NoticeType.BuffOn));
    }

    [Fact]
    public void For_BuffOffIsGray()
    {
        Assert.Equal(new Color(0.7f, 0.7f, 0.7f), NoticeColors.For(NoticeType.BuffOff));
    }

    [Fact]
    public void For_UnknownTypeIsWhite()
    {
        Assert.Equal(GameColors.White, NoticeColors.For((NoticeType)99));
    }
}
