using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using MapEditor.App.Settings;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Google.Auth;
using MapEditor.GameData.Google.Sheets;
using MapEditor.GameData.Sync;

namespace MapEditor.App.Connectivity;

public sealed class GameDataConnectivity
{
    private readonly AppSettingsStore _settings;
    private readonly Func<GoogleConnection> _createConnection;
    private readonly Func<UserCredential, IGameDataGateway> _createGateway;
    private GoogleConnection? _connection;
    private GameDataSyncCoordinator? _coordinator;

    public GameDataConnectivity(
        AppSettingsStore settings,
        Func<GoogleConnection>? connectionFactory = null,
        Func<UserCredential, IGameDataGateway>? gatewayFactory = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _createConnection = connectionFactory ?? (() => new GoogleConnection(new GoogleOAuthClientConfig()));
        _createGateway = gatewayFactory ?? (credential => new GoogleSheetsGateway(GoogleSheetsServiceFactory.Create(credential)));
    }

    public bool IsConnected
    {
        get
        {
            var connection = _connection;
            return connection is not null && connection.IsConnected;
        }
    }

    public GameDataSyncCoordinator? Coordinator => _coordinator;

    public SpreadsheetReference? RememberedSpreadsheet
    {
        get
        {
            var url = _settings.LoadOrDefault().SpreadsheetUrl;
            return SpreadsheetReferenceParser.TryParse(url, out var reference) ? reference : null;
        }
    }

    public bool TryRememberSpreadsheet(string? pastedUrl)
    {
        if (!SpreadsheetReferenceParser.TryParse(pastedUrl, out var reference))
        {
            return false;
        }

        _settings.Update(settings => settings with { SpreadsheetUrl = reference.CanonicalUrl });
        return true;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var connection = _connection ??= _createConnection();
        await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
        // ConnectAsync only flips IsConnected after the broker returned a credential.
        var credential = connection.Credential ?? throw new InvalidOperationException("Google connection has no credential.");
        _coordinator = new GameDataSyncCoordinator(_createGateway(credential));
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var connection = _connection ??= _createConnection();
        await connection.DisconnectAsync().ConfigureAwait(false);
        _coordinator = null;
    }
}
