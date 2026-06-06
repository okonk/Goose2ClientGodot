using System.IO;
using Goose2Client.Character;
using Xunit;

public class AnimationHeightsTests
{
    private static AnimationHeights FromLines(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        return AnimationHeights.Load(path);
    }

    [Fact]
    public void GetHeight_returns_parsed_value()
    {
        var h = FromLines("Body-1-walk-down,48", "Helm-12-idle-up,72");
        Assert.Equal(48, h.GetHeight("Body-1-walk-down"));
        Assert.Equal(72, h.GetHeight("Helm-12-idle-up"));
    }

    [Fact]
    public void GetHeight_defaults_to_64_when_missing()
        => Assert.Equal(64, FromLines("Body-1-walk-down,48").GetHeight("Nope-9-idle-down"));
}
