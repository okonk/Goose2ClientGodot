using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Sheets.v4;
using Google.Apis.Util.Store;

namespace MapEditor.GameData.Google.Auth;

public sealed class GoogleConnection
{
    // Stable across runs and machines so stored tokens survive app updates and reconnects.
    public const string UserKey = "map-editor-user";

    internal delegate Task<UserCredential> AuthorizeAsyncDelegate(
        ClientSecrets clientSecrets,
        IEnumerable<string> scopes,
        string user,
        CancellationToken cancellationToken,
        IDataStore tokenStore,
        ICodeReceiver codeReceiver);

    private readonly GoogleOAuthClientConfig _clientConfig;
    private readonly IDataStore _tokenStore;
    private AuthorizeAsyncDelegate _authorizeAsync = (clientSecrets, scopes, user, cancellationToken, tokenStore, codeReceiver) =>
        GoogleWebAuthorizationBroker.AuthorizeAsync(clientSecrets, scopes, user, cancellationToken, tokenStore, codeReceiver);

    public bool IsConnected { get; private set; }

    public UserCredential? Credential { get; private set; }

    public GoogleConnection(GoogleOAuthClientConfig clientConfig, IDataStore? tokenStore = null)
    {
        _clientConfig = clientConfig ?? throw new ArgumentNullException(nameof(clientConfig));
        // Full path so the broker persists tokens beside settings, not in its default location.
        _tokenStore = tokenStore ?? new FileDataStore(GoogleTokenPathResolver.Resolve(), true);
    }

    internal void SetAuthorizeAsyncForTests(AuthorizeAsyncDelegate authorizeAsync) => _authorizeAsync = authorizeAsync;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        var clientSecrets = _clientConfig.LoadClientSecrets();
        // Loopback IP so the redirect binds to 127.0.0.1, where localhost may resolve to ::1 first.
        var codeReceiver = new LocalServerCodeReceiver(null, LocalServerCodeReceiver.CallbackUriChooserStrategy.ForceLoopbackIp);
        var credential = await _authorizeAsync(
            clientSecrets,
            new[] { SheetsService.Scope.Spreadsheets },
            UserKey,
            cancellationToken,
            _tokenStore,
            codeReceiver).ConfigureAwait(false);

        Credential = credential;
        IsConnected = true;
    }

    public async Task DisconnectAsync()
    {
        await _tokenStore.DeleteAsync<TokenResponse>(UserKey).ConfigureAwait(false);
        Credential = null;
        IsConnected = false;
    }
}
