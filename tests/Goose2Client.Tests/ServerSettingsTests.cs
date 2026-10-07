using Xunit;

namespace Goose2Client.Tests;

public class ServerSettingsTests
{
    [Theory]
    [InlineData("game.illutia.net", "game.illutia.net")]
    [InlineData("  Scyther.Local  ", "scyther.local")]
    [InlineData("http://scyther.local", "scyther.local")]
    [InlineData("HTTPS://Scyther.Local/", "scyther.local")]
    [InlineData("scyther.local/", "scyther.local")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeHost_TrimLowercasesAndStripsSchemeAndSlash(string? raw, string expected)
        => Assert.Equal(expected, ServerSettings.NormalizeHost(raw));

    [Fact]
    public void TryValidate_AcceptsHostAndPort()
    {
        Assert.True(ServerSettings.TryValidate("Scyther.Local", "2007", out var host, out var port, out var error));
        Assert.Equal("scyther.local", host);
        Assert.Equal(2007, port);
        Assert.Null(error);
    }

    [Fact]
    public void TryValidate_BlankPortMeansDefault()
    {
        Assert.True(ServerSettings.TryValidate("scyther.local", "  ", out _, out var port, out _));
        Assert.Equal(ServerSettings.DefaultPort, port);
    }

    [Theory]
    [InlineData("", "2006")]
    [InlineData("bad host", "2006")]
    [InlineData("host:2006", "2006")]
    [InlineData("scyther.local", "abc")]
    [InlineData("scyther.local", "0")]
    [InlineData("scyther.local", "70000")]
    public void TryValidate_RejectsBadInput(string host, string port)
        => Assert.False(ServerSettings.TryValidate(host, port, out _, out _, out var error));

    [Fact]
    public void TryValidate_BadPortResetsToDefault()
    {
        Assert.False(ServerSettings.TryValidate("scyther.local", "99999", out _, out var port, out _));
        Assert.Equal(ServerSettings.DefaultPort, port);
    }

    [Fact]
    public void TryValidate_RejectionCarriesMessage()
    {
        Assert.False(ServerSettings.TryValidate("", "2006", out _, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void FormatLabel_OmitsOnlyTheDefaultPort()
    {
        Assert.Equal("game.illutia.net", ServerSettings.FormatLabel("game.illutia.net", 2006));
        Assert.Equal("scyther.local:2007", ServerSettings.FormatLabel("scyther.local", 2007));
    }
}
