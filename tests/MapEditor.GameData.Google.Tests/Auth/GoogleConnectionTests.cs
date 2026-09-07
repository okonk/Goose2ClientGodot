using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Sheets.v4;
using Google.Apis.Util.Store;
using MapEditor.GameData.Google.Auth;
using Xunit;

namespace MapEditor.GameData.Google.Tests.Auth;

public class GoogleConnectionTests
{
    private const string ValidClientJson = """
        {
          "client": {
            "client_id": "test-client-id",
            "client_secret": "test-client-secret",
            "auth_uri": "https://accounts.google.com/o/oauth2/auth",
            "token_uri": "https://oauth2.googleapis.com/token"
          }
        }
        """;

    [Fact]
    public async Task Connect_PassesExactScope_UserKey_SuppliedStore_LoopbackReceiver_AndCancellationToken()
    {
        await using var temp = await TempDirectory.CreateAsync();
        var clientJsonPath = WriteClientJson(temp.Path, ValidClientJson);
        var store = new RecordingDataStore();
        var connection = CreateConnectedConnection(clientJsonPath, store, out var call);
        var cancellationToken = new CancellationToken();

        await connection.ConnectAsync(cancellationToken);

        Assert.True(connection.IsConnected);
        Assert.NotNull(connection.Credential);
        Assert.Single(call);
        var captured = call[0];
        Assert.Equal(new[] { SheetsService.Scope.Spreadsheets }, captured.Scopes);
        Assert.Equal(GoogleConnection.UserKey, captured.User);
        Assert.Same(store, captured.TokenStore);
        Assert.Equal(cancellationToken, captured.CancellationToken);
        Assert.Equal("test-client-id", captured.ClientSecrets.ClientId);
        Assert.IsType<LocalServerCodeReceiver>(captured.CodeReceiver);
        Assert.Equal("127.0.0.1", new Uri(captured.CodeReceiver.RedirectUri).Host);
    }

    [Fact]
    public async Task Connect_AlreadyConnected_ReturnsExistingState_WithoutCallingBrokerAgain()
    {
        await using var temp = await TempDirectory.CreateAsync();
        var clientJsonPath = WriteClientJson(temp.Path, ValidClientJson);
        var connection = CreateConnectedConnection(clientJsonPath, new RecordingDataStore(), out var call);

        await connection.ConnectAsync();
        var firstCredential = connection.Credential;
        await connection.ConnectAsync();

        Assert.Single(call);
        Assert.Same(firstCredential, connection.Credential);
    }

    [Fact]
    public async Task Connect_AuthorizationFails_PublishesNoCredential()
    {
        await using var temp = await TempDirectory.CreateAsync();
        var clientJsonPath = WriteClientJson(temp.Path, ValidClientJson);
        var connection = CreateConnectedConnection(clientJsonPath, new RecordingDataStore(), out var call);
        connection.SetAuthorizeAsyncForTests((clientSecrets, scopes, user, cancellationToken, tokenStore, codeReceiver) =>
        {
            call.Add(new BrokerCall
            {
                ClientSecrets = clientSecrets,
                Scopes = scopes.ToList(),
                User = user,
                CancellationToken = cancellationToken,
                TokenStore = tokenStore,
                CodeReceiver = codeReceiver
            });
            return Task.FromException<UserCredential>(new TokenResponseException(new TokenErrorResponse { Error = "access_denied" }));
        });

        await Assert.ThrowsAsync<TokenResponseException>(() => connection.ConnectAsync());

        Assert.False(connection.IsConnected);
        Assert.Null(connection.Credential);
        Assert.Single(call);
    }

    [Fact]
    public async Task Connect_CancelledToken_IsPropagatedToBroker_AndPublishesNoCredential()
    {
        await using var temp = await TempDirectory.CreateAsync();
        var clientJsonPath = WriteClientJson(temp.Path, ValidClientJson);
        var connection = CreateConnectedConnection(clientJsonPath, new RecordingDataStore(), out var call);
        var cancelled = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.ConnectAsync(cancelled));

        Assert.True(call[0].CancellationToken.IsCancellationRequested);
        Assert.False(connection.IsConnected);
        Assert.Null(connection.Credential);
    }

    [Fact]
    public async Task Connect_MissingClientJson_ThrowsConfigurationException_NamingPath_WithoutCallingBroker()
    {
        await using var temp = await TempDirectory.CreateAsync();
        var missingPath = Path.Combine(temp.Path, "does-not-exist.json");
        var store = new RecordingDataStore();
        var calls = new List<BrokerCall>();
        var connection = new GoogleConnection(new GoogleOAuthClientConfig(missingPath, temp.Path), store);
        AttachFakeBroker(connection, calls);

        var exception = await Assert.ThrowsAsync<GoogleOAuthClientConfigurationException>(
            () => connection.ConnectAsync());

        Assert.Contains(missingPath, exception.Message);
        Assert.Empty(calls);
        Assert.False(connection.IsConnected);
        Assert.Null(connection.Credential);
    }

    [Fact]
    public async Task Disconnect_ClearsCredential_AndDeletesOnlyStableKey()
    {
        await using var temp = await TempDirectory.CreateAsync();
        var clientJsonPath = WriteClientJson(temp.Path, ValidClientJson);
        var store = new RecordingDataStore();
        var connection = CreateConnectedConnection(clientJsonPath, store, out _);
        await connection.ConnectAsync();

        await connection.DisconnectAsync();

        Assert.False(connection.IsConnected);
        Assert.Null(connection.Credential);
        Assert.Equal(1, store.DeleteCalls);
        Assert.Equal(GoogleConnection.UserKey, store.DeletedKey);
    }

    [Fact]
    public async Task Disconnect_StoreDeleteFails_Throws_AndKeepsConnectedState()
    {
        await using var temp = await TempDirectory.CreateAsync();
        var clientJsonPath = WriteClientJson(temp.Path, ValidClientJson);
        var store = new RecordingDataStore { DeleteFailure = new IOException("disk full") };
        var connection = CreateConnectedConnection(clientJsonPath, store, out _);
        await connection.ConnectAsync();

        await Assert.ThrowsAsync<IOException>(() => connection.DisconnectAsync());

        Assert.True(connection.IsConnected);
        Assert.NotNull(connection.Credential);
    }

    private static GoogleConnection CreateConnectedConnection(
        string clientJsonPath,
        RecordingDataStore store,
        out List<BrokerCall> call)
    {
        call = new List<BrokerCall>();
        var connection = new GoogleConnection(new GoogleOAuthClientConfig(clientJsonPath, Path.GetDirectoryName(clientJsonPath)!), store);
        AttachFakeBroker(connection, call);
        return connection;
    }

    private static void AttachFakeBroker(GoogleConnection connection, List<BrokerCall> call)
    {
        connection.SetAuthorizeAsyncForTests((clientSecrets, scopes, user, cancellationToken, tokenStore, codeReceiver) =>
        {
            call.Add(new BrokerCall
            {
                ClientSecrets = clientSecrets,
                Scopes = scopes.ToList(),
                User = user,
                CancellationToken = cancellationToken,
                TokenStore = tokenStore,
                CodeReceiver = codeReceiver
            });
            return cancellationToken.IsCancellationRequested
                ? Task.FromException<UserCredential>(new TaskCanceledException())
                : Task.FromResult(CreateUserCredential());
        });
    }

    private static UserCredential CreateUserCredential()
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = "test-client-id", ClientSecret = "test-client-secret" },
            Scopes = new[] { SheetsService.Scope.Spreadsheets }
        });
        return new UserCredential(flow, GoogleConnection.UserKey, new TokenResponse
        {
            AccessToken = "test-access-token",
            RefreshToken = "test-refresh-token"
        });
    }

    private static string WriteClientJson(string directory, string json)
    {
        var path = Path.Combine(directory, "google-oauth-client.json");
        File.WriteAllText(path, json);
        return path;
    }

    private sealed class BrokerCall
    {
        public required ClientSecrets ClientSecrets { get; init; }
        public required IReadOnlyList<string> Scopes { get; init; }
        public required string User { get; init; }
        public required CancellationToken CancellationToken { get; init; }
        public required IDataStore TokenStore { get; init; }
        public required ICodeReceiver CodeReceiver { get; init; }
    }

    private sealed class RecordingDataStore : IDataStore
    {
        public int DeleteCalls { get; private set; }
        public string? DeletedKey { get; private set; }
        public Exception? DeleteFailure { get; set; }

        public Task StoreAsync<T>(string key, T value) => Task.CompletedTask;

        public Task DeleteAsync<T>(string key)
        {
            DeleteCalls++;
            DeletedKey = key;
            return DeleteFailure is null
                ? Task.CompletedTask
                : Task.FromException(DeleteFailure);
        }

        public Task<T> GetAsync<T>(string key) => Task.FromResult(default(T)!);

        public Task ClearAsync() => Task.CompletedTask;
    }

    private sealed class TempDirectory : IAsyncDisposable
    {
        private readonly string _path;

        private TempDirectory(string path) => _path = path;

        public string Path => _path;

        public static async Task<TempDirectory> CreateAsync()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "google-connection-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            await Task.CompletedTask;
            return new TempDirectory(path);
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(_path, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    public class ClientConfig
    {
        [Fact]
        public void ResolveClientJsonPath_PrefersEnvironmentVariablePath_OverBesideExecutable()
        {
            var config = new GoogleOAuthClientConfig("/custom/oauth-client.json", "/app");

            Assert.Equal("/custom/oauth-client.json", config.ResolveClientJsonPath());
        }

        [Fact]
        public void ResolveClientJsonPath_FallsBackToBesideExecutable_WhenNoEnvironmentPath()
        {
            var config = new GoogleOAuthClientConfig(null, "/app");

            Assert.Equal(Path.Combine("/app", "google-oauth-client.json"), config.ResolveClientJsonPath());
        }

        [Fact]
        public void ResolveClientJsonPath_IgnoresNonRootedEnvironmentPath()
        {
            var config = new GoogleOAuthClientConfig("relative/oauth-client.json", "/app");

            Assert.Equal(Path.Combine("/app", "google-oauth-client.json"), config.ResolveClientJsonPath());
        }

        [Theory]
        [InlineData("""
            {
              "client": {
                "client_id": "abc",
                "client_secret": "s3cr3t",
                "auth_uri": "https://accounts.google.com/o/oauth2/auth",
                "token_uri": "https://oauth2.googleapis.com/token"
              }
            }
            """)]
        [InlineData("""
            {
              "installed": {
                "client_id": "abc",
                "client_secret": "s3cr3t",
                "auth_uri": "https://accounts.google.com/o/oauth2/auth",
                "token_uri": "https://oauth2.googleapis.com/token"
              }
            }
            """)]
        public void LoadClientSecrets_ValidDesktopClient_ReturnsSecrets(string json)
        {
            var clientJsonPath = WriteClientJson(json);

            var secrets = new GoogleOAuthClientConfig(clientJsonPath, Path.GetDirectoryName(clientJsonPath)!).LoadClientSecrets();

            Assert.Equal("abc", secrets.ClientId);
            Assert.Equal("s3cr3t", secrets.ClientSecret);
        }

        [Fact]
        public void LoadClientSecrets_MissingClientId_ThrowsConfigurationException_NamingPath()
        {
            var clientJsonPath = WriteClientJson("""
                {
                  "client": {
                    "client_secret": "s3cr3t",
                    "auth_uri": "https://accounts.google.com/o/oauth2/auth",
                    "token_uri": "https://oauth2.googleapis.com/token"
                  }
                }
                """);

            var exception = Assert.Throws<GoogleOAuthClientConfigurationException>(
                () => new GoogleOAuthClientConfig(clientJsonPath, Path.GetDirectoryName(clientJsonPath)!).LoadClientSecrets());

            Assert.Contains(clientJsonPath, exception.Message);
        }

        [Fact]
        public void LoadClientSecrets_WebClient_ThrowsConfigurationException()
        {
            var clientJsonPath = WriteClientJson("""
                {
                  "web": {
                    "client_id": "abc",
                    "client_secret": "s3cr3t",
                    "redirect_uris": ["https://example.com/callback"]
                  }
                }
                """);

            var exception = Assert.Throws<GoogleOAuthClientConfigurationException>(
                () => new GoogleOAuthClientConfig(clientJsonPath, Path.GetDirectoryName(clientJsonPath)!).LoadClientSecrets());

            Assert.Contains(clientJsonPath, exception.Message);
        }

        [Fact]
        public void LoadClientSecrets_InvalidJson_Throws_WithoutLeakingFileContents()
        {
            const string sentinel = "SENTINEL-CONTENT-DO-NOT-LEAK";
            var clientJsonPath = WriteClientJson("{ \"" + sentinel + "\": oops");

            var exception = Assert.Throws<GoogleOAuthClientConfigurationException>(
                () => new GoogleOAuthClientConfig(clientJsonPath, Path.GetDirectoryName(clientJsonPath)!).LoadClientSecrets());

            Assert.Contains(clientJsonPath, exception.Message);
            Assert.DoesNotContain(sentinel, exception.Message);
        }

        [Fact]
        public void LoadClientSecrets_MissingFile_ThrowsConfigurationException_NamingPath()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), "google-connection-tests-missing-" + Guid.NewGuid().ToString("N") + ".json");

            var exception = Assert.Throws<GoogleOAuthClientConfigurationException>(
                () => new GoogleOAuthClientConfig(missingPath, Path.GetDirectoryName(missingPath)!).LoadClientSecrets());

            Assert.Contains(missingPath, exception.Message);
        }

        private static string WriteClientJson(string json)
        {
            var directory = Path.Combine(Path.GetTempPath(), "google-connection-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "google-oauth-client.json");
            File.WriteAllText(path, json);
            return path;
        }
    }
}
