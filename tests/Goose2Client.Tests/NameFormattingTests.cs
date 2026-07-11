using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests;

public class NameFormattingTests
{
    [Fact]
    public void FullName_allThreeParts_returnsTitleNameSurname()
    {
        Assert.Equal("Sir Bob the Brave", NameFormatting.FullName("Sir", "Bob", "the Brave"));
    }

    [Fact]
    public void FullName_blankSurrounds_returnsNameOnly()
    {
        Assert.Equal("Bob", NameFormatting.FullName("", "Bob", ""));
    }

    [Fact]
    public void FullName_nullSurrounds_returnsNameOnly()
    {
        Assert.Equal("Bob", NameFormatting.FullName(null, "Bob", null));
    }

    [Fact]
    public void FullName_surnameOnly_returnsNameSurname()
    {
        Assert.Equal("Bob the Brave", NameFormatting.FullName(null, "Bob", "the Brave"));
    }

    [Fact]
    public void FullName_titleOnly_returnsTitleName()
    {
        Assert.Equal("Sir Bob", NameFormatting.FullName("Sir", "Bob", null));
    }
}
