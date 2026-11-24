using System.Globalization;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class TextInputInfoPacketTests
{
    [Fact]
    public void Txi_ParsesFullPacket()
    {
        var p = (TextInputInfoPacket)new TextInputInfoPacket().Parse(new PacketParser("TXI7|16|Rename|Old|New name:", "TXI"));
        Assert.Equal(7, p.WindowId);
        Assert.Equal(16, p.MaxLength);
        Assert.Equal("Rename", p.ButtonText);
        Assert.Equal("Old", p.DefaultText);
        Assert.Equal("New name:", p.Prompt);
    }

    [Fact]
    public void Txi_ParsesEmptyDefaultText()
    {
        var p = (TextInputInfoPacket)new TextInputInfoPacket().Parse(new PacketParser("TXI7|16|Rename||New name:", "TXI"));
        Assert.Equal(7, p.WindowId);
        Assert.Equal(16, p.MaxLength);
        Assert.Equal("Rename", p.ButtonText);
        Assert.Equal("", p.DefaultText);
        Assert.Equal("New name:", p.Prompt);
    }

    [Fact]
    public void Txi_ParsesPromptContainingDelimeter()
    {
        var p = (TextInputInfoPacket)new TextInputInfoPacket().Parse(new PacketParser("TXI7|16|Rename||Pick | choose", "TXI"));
        Assert.Equal("Pick | choose", p.Prompt);
    }

    [Fact]
    public void Txi_ParsesEmptyPromptWithoutThrowing()
    {
        var p = (TextInputInfoPacket)new TextInputInfoPacket().Parse(new PacketParser("TXI7|16|Rename|Old|", "TXI"));
        Assert.Equal("", p.Prompt);
    }

    [Fact]
    public void FormatTxt_FormatsSubmission()
    {
        Assert.Equal("TXT7,hello", TextInputInfoPacket.FormatTxt(7, "hello"));
    }

    [Fact]
    public void FormatTxt_KeepsCommasInText()
    {
        Assert.Equal("TXT7,a,b,c", TextInputInfoPacket.FormatTxt(7, "a,b,c"));
    }

    [Fact]
    public void FormatTxt_FormatsInvariant_UnderNonEnglishCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fa-IR");
            Assert.Equal("TXT7,hello", TextInputInfoPacket.FormatTxt(7, "hello"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
