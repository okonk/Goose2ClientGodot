using System.Globalization;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class AmountInfoPacketTests
{
    [Fact]
    public void Ami_ParsesFullPacket()
    {
        var p = (AmountInfoPacket)new AmountInfoPacket().Parse(new PacketParser("AMI7|1|42|0|0|0|0|1|237|237|Donate how many Logs? You have 237.", "AMI"));
        Assert.Equal(7, p.WindowId);
        Assert.Equal(1, p.Sheet);
        Assert.Equal(42, p.Graphic);
        Assert.Equal(0, p.R);
        Assert.Equal(0, p.G);
        Assert.Equal(0, p.B);
        Assert.Equal(0, p.A);
        Assert.Equal(1, p.Min);
        Assert.Equal(237, p.Max);
        Assert.Equal(237, p.Default);
        Assert.Equal("Donate how many Logs? You have 237.", p.Prompt);
    }

    [Fact]
    public void Ami_ParsesPromptContainingDelimeter()
    {
        var p = (AmountInfoPacket)new AmountInfoPacket().Parse(new PacketParser("AMI7|0|0|0|0|0|0|1|5|1|Pick | choose", "AMI"));
        Assert.Equal("Pick | choose", p.Prompt);
    }

    [Fact]
    public void Ami_ParsesEmptyPromptWithoutThrowing()
    {
        var p = (AmountInfoPacket)new AmountInfoPacket().Parse(new PacketParser("AMI7|0|0|0|0|0|0|1|5|1|", "AMI"));
        Assert.Equal("", p.Prompt);
    }

    [Fact]
    public void Ami_ParsesLargeValuesAsLong()
    {
        var p = (AmountInfoPacket)new AmountInfoPacket().Parse(new PacketParser("AMI7|0|0|0|0|0|0|1|9000000000|9000000000", "AMI"));
        Assert.Equal(1L, p.Min);
        Assert.Equal(9_000_000_000L, p.Max);
        Assert.Equal(9_000_000_000L, p.Default);
    }

    [Fact]
    public void FormatAmt_FormatsConfirmation()
    {
        Assert.Equal("AMT7,5", AmountInfoPacket.FormatAmt(7, 5));
    }

    [Fact]
    public void FormatAmt_FormatsInvariant_UnderNonEnglishCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fa-IR");
            Assert.Equal("AMT7,5", AmountInfoPacket.FormatAmt(7, 5));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
