using Goose2Client.Network;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class AdminModeActivatePacketTests
{
    [Fact]
    public void Parse_ServersFormat_ReadsLoginIdAndEnabled()
    {
        var p = (AdminModeActivatePacket)new AdminModeActivatePacket().Parse(new PacketParser("AMA123,1", "AMA"));
        Assert.Equal(123, p.LoginId);
        Assert.Equal(1, p.Enabled);
    }

    [Fact]
    public void Parse_Deactivate_EnabledIsZero()
    {
        var p = (AdminModeActivatePacket)new AdminModeActivatePacket().Parse(new PacketParser("AMA42,0", "AMA"));
        Assert.Equal(42, p.LoginId);
        Assert.Equal(0, p.Enabled);
    }
}
