using Godot;
using Goose2Client;
using Xunit;

public class ScreenOrientationSettingTests
{
    [Fact]
    public void IndexOf_KnownOrientation_FindsChoice()
        => Assert.Equal(3, ScreenOrientationSetting.IndexOf(DisplayServer.ScreenOrientation.Portrait));

    [Fact]
    public void IndexOf_UnlistedOrientation_FallsBackToLandscape()
        => Assert.Equal(0, ScreenOrientationSetting.IndexOf(DisplayServer.ScreenOrientation.Sensor));
}
