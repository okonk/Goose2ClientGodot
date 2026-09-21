using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Tests;

public class PacketManagerDiagnosticsTests
{
    [Fact]
    public void IdentifyPrefix_ReturnsRegisteredPrefixWithoutPayload()
    {
        var manager = new PacketManager();
        manager.Listen<SendCurrentMapPacket>(_ => { });

        string prefix = manager.IdentifyPrefix("SCMsecret-map,12,secret-name");

        Assert.Equal("SCM", prefix);
    }

    [Fact]
    public void IdentifyPrefix_ReturnsUnknownForUnregisteredPacket()
    {
        var manager = new PacketManager();

        string prefix = manager.IdentifyPrefix("PRIVATE secret text");

        Assert.Equal("unknown", prefix);
    }
}
