using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Sheets.v4;
using MapEditor.GameData.Google.Sheets;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Schema;
using Xunit;

namespace MapEditor.GameData.Google.Tests.Sheets;

public class GoogleSheetsTransportIntegrationTests
{
    private const string SpreadsheetId = "test-spreadsheet";
    private const string AccessToken = "test-access-token";

    private static readonly Dictionary<string, string[][]> SheetRows = new(StringComparer.Ordinal)
    {
        ["Maps"] =
        [
            ["1", "Map One", "map_one"],
            ["2", "Map Two", "map_two"]
        ],
        ["NPCs"] =
        [
            ["10", "2", "Goose"]
        ],
        ["NPC Spawns"] =
        [
            ["10", "1", "5", "6"],
            ["11", "1", "7", "8"],
            ["12", "2", "9", "9"]
        ],
        ["Warptiles"] =
        [
            ["1", "1", "1", "2", "2", "2"],
            ["2", "3", "3", "1", "4", "4"]
        ]
    };

    [Fact]
    public async Task ReadGameData_RealTransport_MapsRowsAndSendsBearerTokenWithEscapedRanges()
    {
        await using var server = await LoopbackSheetsServer.StartAsync();
        var gateway = CreateGateway(server);

        var data = await gateway.ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                new MapReference(1, "Map One", "map_one"),
                new MapReference(2, "Map Two", "map_two")
            },
            data.Maps);
        var npc = Assert.Single(data.Npcs.Values);
        Assert.Equal(10, npc.NpcId);
        Assert.Equal("Goose", npc.NpcName);
        Assert.Equal(3, data.Spawns.Count);
        Assert.Equal(2, data.Spawns[0].WorksheetRowNumber);
        Assert.Equal(new NpcSpawnRow(10, 1, 5, 6), data.Spawns[0].Value);
        Assert.Equal(4, data.Spawns[2].WorksheetRowNumber);
        Assert.Equal(2, data.Warps.Count);
        Assert.Equal(2, data.Warps[0].WorksheetRowNumber);
        Assert.Equal(new WarpRow(1, 1, 1, 2, 2, 2), data.Warps[0].Value);

        Assert.All(server.Requests, request => Assert.Equal($"Bearer {AccessToken}", request.Authorization));
        var batchGet = server.Requests.Single(request => request.Path.Contains("values:batchGet"));
        Assert.Contains("%27NPC", batchGet.RawQuery);
        var decodedQuery = Uri.UnescapeDataString(batchGet.RawQuery);
        Assert.Contains("ranges='Maps'!A1:Q", decodedQuery);
        Assert.Contains("ranges='NPCs'!A1:BG", decodedQuery);
        Assert.Contains("ranges='NPC Spawns'!A1:D", decodedQuery);
        Assert.Contains("ranges='Warptiles'!A1:F", decodedQuery);
    }

    [Fact]
    public async Task ReadOwnedRows_RealTransport_ReturnsOnlyOwnedRows()
    {
        await using var server = await LoopbackSheetsServer.StartAsync();
        var gateway = CreateGateway(server);

        var owned = await gateway.ReadOwnedRowsAsync(SpreadsheetId, 1, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(10, 1, 5, 6)),
                new RemoteRow<NpcSpawnRow>(3, new NpcSpawnRow(11, 1, 7, 8))
            },
            owned.Spawns);
        Assert.Equal(
            new[] { new RemoteRow<WarpRow>(2, new WarpRow(1, 1, 1, 2, 2, 2)) },
            owned.Warps);
        var batchGet = server.Requests.Single(request => request.Path.Contains("values:batchGet"));
        var decodedQuery = Uri.UnescapeDataString(batchGet.RawQuery);
        Assert.Contains("ranges='Maps'!A1:Q1", decodedQuery);
        Assert.Contains("ranges='NPCs'!A1:BG1", decodedQuery);
    }

    [Fact]
    public async Task ReplaceOwnedRows_RealTransport_SendsExactlyOneBatchUpdateWithDescendingDeletesAndBothAppends()
    {
        await using var server = await LoopbackSheetsServer.StartAsync();
        var gateway = CreateGateway(server);
        var spawnPlan = new ReplacementPlan(
            "NPC Spawns",
            new[] { new RowDelete(3), new RowDelete(5) },
            new[] { new RowInsert(new string?[] { "10", "1", "5", "6" }) });
        var warpPlan = new ReplacementPlan(
            "Warptiles",
            new[] { new RowDelete(4) },
            new[] { new RowInsert(new string?[] { "1", "2", "3", "9", "4", "5" }) });

        await gateway.ReplaceOwnedRowsAsync(SpreadsheetId, spawnPlan, warpPlan, CancellationToken.None);

        var posts = server.Requests.Where(request => request.Method == "POST").ToList();
        var post = Assert.Single(posts);
        Assert.Equal($"/v4/spreadsheets/{SpreadsheetId}:batchUpdate", post.Path);
        Assert.Equal($"Bearer {AccessToken}", post.Authorization);

        var batchUpdateBody = server.LastBatchUpdateBody;
        Assert.NotNull(batchUpdateBody);
        using var document = JsonDocument.Parse(batchUpdateBody);
        var requests = document.RootElement.GetProperty("requests").EnumerateArray().ToList();
        Assert.Equal(5, requests.Count);

        var spawnDeletes = requests.Take(2)
            .Select(request => request.GetProperty("deleteDimension").GetProperty("range"))
            .ToList();
        Assert.All(spawnDeletes, range => Assert.Equal("ROWS", range.GetProperty("dimension").GetString()));
        Assert.All(spawnDeletes, range => Assert.Equal(103, range.GetProperty("sheetId").GetInt32()));
        Assert.Equal(4, spawnDeletes[0].GetProperty("startIndex").GetInt32());
        Assert.Equal(5, spawnDeletes[0].GetProperty("endIndex").GetInt32());
        Assert.Equal(2, spawnDeletes[1].GetProperty("startIndex").GetInt32());
        Assert.Equal(3, spawnDeletes[1].GetProperty("endIndex").GetInt32());

        var spawnAppend = requests[2].GetProperty("appendCells");
        Assert.Equal(103, spawnAppend.GetProperty("sheetId").GetInt32());
        Assert.Equal("userEnteredValue", spawnAppend.GetProperty("fields").GetString());
        Assert.Equal(
            new[] { "10", "1", "5", "6" },
            AppendCellValues(spawnAppend));

        var warpDelete = requests[3].GetProperty("deleteDimension").GetProperty("range");
        Assert.Equal(104, warpDelete.GetProperty("sheetId").GetInt32());
        Assert.Equal("ROWS", warpDelete.GetProperty("dimension").GetString());
        Assert.Equal(3, warpDelete.GetProperty("startIndex").GetInt32());
        Assert.Equal(4, warpDelete.GetProperty("endIndex").GetInt32());

        var warpAppend = requests[4].GetProperty("appendCells");
        Assert.Equal(104, warpAppend.GetProperty("sheetId").GetInt32());
        Assert.Equal("userEnteredValue", warpAppend.GetProperty("fields").GetString());
        Assert.Equal(
            new[] { "1", "2", "3", "9", "4", "5" },
            AppendCellValues(warpAppend));
    }

    private static List<string> AppendCellValues(JsonElement appendCells)
        => appendCells.GetProperty("rows")[0]
            .GetProperty("values")
            .EnumerateArray()
            .Select(cell => cell.GetProperty("userEnteredValue").GetProperty("stringValue").GetString()!)
            .ToList();

    private static GoogleSheetsGateway CreateGateway(LoopbackSheetsServer server)
    {
        var service = GoogleSheetsServiceFactory.Create(CreateFreshCredential(), $"http://127.0.0.1:{server.Port}/");
        return new GoogleSheetsGateway(service);
    }

    private static UserCredential CreateFreshCredential()
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret"
            },
            Scopes = new[] { SheetsService.Scope.Spreadsheets }
        });
        // Far-future expiry so the flow is never exercised and only API calls reach the server.
        return new UserCredential(flow, "test-user", new TokenResponse
        {
            AccessToken = AccessToken,
            TokenType = "Bearer",
            IssuedUtc = DateTime.UtcNow,
            ExpiresInSeconds = 365 * 24 * 3600,
            Scope = SheetsService.Scope.Spreadsheets
        });
    }

    private sealed class LoopbackSheetsServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly Task _loop;
        private static readonly GameDataSchema Schema = GameDataSchema.LoadEmbedded();

        private LoopbackSheetsServer(HttpListener listener, int port)
        {
            _listener = listener;
            Port = port;
            _loop = Task.Run(() => HandleLoopAsync());
        }

        public int Port { get; }

        public List<RecordedRequest> Requests { get; } = new();

        public string? LastBatchUpdateBody { get; private set; }

        public static async Task<LoopbackSheetsServer> StartAsync()
        {
            var port = GetFreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var server = new LoopbackSheetsServer(listener, port);
            return server;
        }

        private static int GetFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task HandleLoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    break;
                }
                try
                {
                    var body = await ReadBodyAsync(context.Request);
                    Requests.Add(new RecordedRequest(
                        context.Request.HttpMethod,
                        context.Request.Url!.AbsolutePath,
                        context.Request.Url.Query,
                        context.Request.Headers["Authorization"],
                        body));
                    var response = Route(context.Request.HttpMethod, context.Request.Url.AbsolutePath,
                        context.Request.Url.Query, body);
                    var bytes = Encoding.UTF8.GetBytes(response);
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
                catch (Exception)
                {
                    try
                    {
                        context.Response.Abort();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private string Route(string method, string path, string query, string body)
        {
            if (method == "POST" && path == $"/v4/spreadsheets/{SpreadsheetId}:batchUpdate")
            {
                LastBatchUpdateBody = body;
                return $$"""{"spreadsheetId":"{{SpreadsheetId}}","replies":[]}""";
            }
            if (method == "GET" && path == $"/v4/spreadsheets/{SpreadsheetId}/values:batchGet")
            {
                return BuildValuesJson(ParseRanges(query));
            }
            if (method == "GET" && path == $"/v4/spreadsheets/{SpreadsheetId}")
            {
                return """
                    {
                      "spreadsheetId": "test-spreadsheet",
                      "sheets": [
                        { "properties": { "sheetId": 101, "title": "Maps" } },
                        { "properties": { "sheetId": 102, "title": "NPCs" } },
                        { "properties": { "sheetId": 103, "title": "NPC Spawns" } },
                        { "properties": { "sheetId": 104, "title": "Warptiles" } }
                      ]
                    }
                    """;
            }
            throw new InvalidOperationException($"Unexpected request: {method} {path}");
        }

        private static List<string> ParseRanges(string query)
        {
            var ranges = new List<string>();
            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                var parts = pair.Split('=');
                if (parts.Length == 2 && parts[0] == "ranges")
                {
                    ranges.Add(Uri.UnescapeDataString(parts[1]));
                }
            }
            return ranges;
        }

        private static string BuildValuesJson(IReadOnlyList<string> ranges)
        {
            var valueRanges = new List<object>();
            foreach (var range in ranges)
            {
                var title = range[(range.IndexOf('\'') + 1)..range.IndexOf('\'', 1)];
                var header = Schema.GetRequiredSheet(title).Columns.Select(column => column.Header).ToArray();
                valueRanges.Add(new
                {
                    range,
                    majorDimension = "ROWS",
                    values = new[] { header }.Concat(SheetRows[title]).ToArray()
                });
            }
            return JsonSerializer.Serialize(new { valueRanges });
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _loop;
            }
            catch (Exception)
            {
            }
        }

        private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
        {
            var stream = request.InputStream;
            if (string.Equals(request.Headers["Content-Encoding"], "gzip", StringComparison.OrdinalIgnoreCase))
            {
                stream = new GZipStream(stream, CompressionMode.Decompress);
            }
            using var reader = new StreamReader(stream, request.ContentEncoding);
            return await reader.ReadToEndAsync();
        }
    }

    private sealed record RecordedRequest(string Method, string Path, string RawQuery, string? Authorization, string Body);
}
