using System;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class NoticePacketTests
{
    [Fact]
    public void Ntf_ParsesQuestNotice()
    {
        var p = (NoticePacket)new NoticePacket().Parse(new PacketParser("NTF1,Quest Credit: The Hunter's Request (3/10)", "NTF"));
        Assert.Equal(NoticeType.Quest, p.Type);
        Assert.Equal("Quest Credit: The Hunter's Request (3/10)", p.Text);
    }

    [Fact]
    public void Ntf_ParsesTextContainingDelimeter()
    {
        var p = (NoticePacket)new NoticePacket().Parse(new PacketParser("NTF2,You feel strong, and fast.", "NTF"));
        Assert.Equal(NoticeType.BuffOn, p.Type);
        Assert.Equal("You feel strong, and fast.", p.Text);
    }

    [Fact]
    public void Ntf_ParsesEmptyTextWithoutThrowing()
    {
        var p = (NoticePacket)new NoticePacket().Parse(new PacketParser("NTF0,", "NTF"));
        Assert.Equal(NoticeType.Info, p.Type);
        Assert.Equal("", p.Text);
    }

    [Fact]
    public void Ntf_ParsesMissingCommaWithoutThrowing()
    {
        var p = (NoticePacket)new NoticePacket().Parse(new PacketParser("NTF0", "NTF"));
        Assert.Equal(NoticeType.Info, p.Type);
        Assert.Equal("", p.Text);
    }

    [Fact]
    public void Ntf_ParsesUnknownTypeWithoutThrowing()
    {
        var p = (NoticePacket)new NoticePacket().Parse(new PacketParser("NTF9,hi", "NTF"));
        Assert.Equal(9, (int)p.Type);
        Assert.Equal("hi", p.Text);
    }

    [Fact]
    public void Ntf_ThrowsOnNonNumericType()
    {
        Assert.ThrowsAny<Exception>(() => new NoticePacket().Parse(new PacketParser("NTFx,hi", "NTF")));
    }
}
