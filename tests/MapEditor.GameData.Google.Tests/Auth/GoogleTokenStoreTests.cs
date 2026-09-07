using System;
using System.IO;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using MapEditor.GameData.Google.Auth;
using Xunit;

namespace MapEditor.GameData.Google.Tests.Auth;

public class GoogleTokenStoreTests
{
    [Fact]
    public async Task FileDataStore_RoundTripsTokenResponse_InTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "google-token-store-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileDataStore(directory, true);
            var token = new TokenResponse { AccessToken = "access", RefreshToken = "refresh" };

            await store.StoreAsync(GoogleConnection.UserKey, token);
            var loaded = await store.GetAsync<TokenResponse>(GoogleConnection.UserKey);

            Assert.Equal("access", loaded.AccessToken);
            Assert.Equal("refresh", loaded.RefreshToken);

            await store.DeleteAsync<TokenResponse>(GoogleConnection.UserKey);
            Assert.Null(await store.GetAsync<TokenResponse>(GoogleConnection.UserKey));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Disconnect_DeletesOnlyStableKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), "google-token-store-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileDataStore(directory, true);
            await store.StoreAsync(GoogleConnection.UserKey, new TokenResponse { AccessToken = "stable" });
            await store.StoreAsync("other-user", new TokenResponse { AccessToken = "other" });
            var connection = new GoogleConnection(
                new GoogleOAuthClientConfig(Path.Combine(directory, "google-oauth-client.json"), directory),
                store);

            await connection.DisconnectAsync();

            Assert.Null(await store.GetAsync<TokenResponse>(GoogleConnection.UserKey));
            Assert.Equal("other", (await store.GetAsync<TokenResponse>("other-user"))!.AccessToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public class TokenPathResolver
    {
        [Fact]
        public void ResolveWindows_UsesApplicationDataRoot()
        {
            var path = GoogleTokenPathResolver.ResolveWindows(@"C:\Users\tester\AppData\Roaming");

            Assert.Equal(Path.Combine(@"C:\Users\tester\AppData\Roaming", "Goose2MapEditor", "google-tokens"), path);
        }

        [Fact]
        public void ResolveMacOs_UsesApplicationSupportRoot()
        {
            var path = GoogleTokenPathResolver.ResolveMacOs("/Users/tester/Library/Application Support");

            Assert.Equal("/Users/tester/Library/Application Support/Goose2MapEditor/google-tokens", path);
        }

        [Theory]
        [InlineData("/custom/config", "/home/tester", "/custom/config/goose2-map-editor/google-tokens")]
        [InlineData(null, "/home/tester", "/home/tester/.config/goose2-map-editor/google-tokens")]
        [InlineData("", "/home/tester", "/home/tester/.config/goose2-map-editor/google-tokens")]
        [InlineData("relative/config", "/home/tester", "/home/tester/.config/goose2-map-editor/google-tokens")]
        public void ResolveLinux_FollowsXdgConvention(string? xdgConfigHome, string homeDirectory, string expected)
        {
            Assert.Equal(expected, GoogleTokenPathResolver.ResolveLinux(xdgConfigHome, homeDirectory));
        }
    }
}
