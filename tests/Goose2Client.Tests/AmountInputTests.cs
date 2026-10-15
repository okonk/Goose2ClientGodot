using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class AmountInputTests
{
    [Fact]
    public void Parse_Empty_ReturnsNull()
    {
        Assert.Null(AmountInput.Parse("", 1, 100));
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsNull()
    {
        Assert.Null(AmountInput.Parse("   ", 1, 100));
    }

    [Fact]
    public void Parse_NonDigitsStripped()
    {
        Assert.Equal(123, AmountInput.Parse("1,2a3", 0, 1000));
    }

    [Fact]
    public void Parse_WithinRange_ReturnsValue()
    {
        Assert.Equal(50, AmountInput.Parse("50", 1, 100));
    }

    [Fact]
    public void Parse_AboveMax_ClampsToMax()
    {
        Assert.Equal(100, AmountInput.Parse("999", 1, 100));
    }

    [Fact]
    public void Parse_BelowMin_ClampsToMin()
    {
        Assert.Equal(10, AmountInput.Parse("1", 10, 100));
    }

    [Fact]
    public void Parse_OverflowString_ReturnsMax()
    {
        Assert.Equal(100, AmountInput.Parse(new string('9', 30), 1, 100));
    }
}
