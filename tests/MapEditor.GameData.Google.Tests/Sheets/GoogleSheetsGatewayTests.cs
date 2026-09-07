using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Apis.Sheets.v4.Data;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Google.Sheets;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Schema;
using Xunit;

namespace MapEditor.GameData.Google.Tests.Sheets;

public class GoogleSheetsGatewayTests
{
    private const string SpreadsheetId = "spreadsheet-1";

    private static readonly GameDataSchema Schema = GameDataSchema.LoadEmbedded();

    private static GoogleSheetsGateway CreateGateway(FakeSheetsTransport operations)
        => new(operations, Schema);

    private static Spreadsheet Metadata(params (string Title, int Id)[] sheets) => new()
    {
        SpreadsheetId = SpreadsheetId,
        Sheets = sheets.Select(sheet => new Sheet
        {
            Properties = new SheetProperties { SheetId = sheet.Id, Title = sheet.Title }
        }).ToList()
    };

    private static Spreadsheet ValidMetadata() => Metadata(
        ("Maps", 0), ("NPCs", 1), ("NPC Spawns", 2), ("Warptiles", 3));

    private static string?[] HeaderRow(string sheetName)
        => Schema.GetRequiredSheet(sheetName).Columns.Select(column => column.Header).ToArray();

    private static object?[] Row(string sheetName, params (string Name, string Value)[] values)
    {
        var sheet = Schema.GetRequiredSheet(sheetName);
        var row = new object?[sheet.Columns.Count];
        foreach (var (name, value) in values)
        {
            row[sheet.GetColumnIndex(name)] = value;
        }
        return row;
    }

    private static object?[] Truncate(object?[] row, int length) => row.Take(length).ToArray();

    private static BatchGetValuesResponse Values(params (string Range, object?[][] Rows)[] ranges) => new()
    {
        ValueRanges = ranges.Select(range => new ValueRange
        {
            Range = range.Range,
            Values = ToILists(range.Rows)
        }).ToList()
    };

    private static IList<IList<object>> ToILists(object?[][] rows)
    {
        var result = new List<IList<object>>(rows.Length);
        foreach (var row in rows)
        {
            var cells = new List<object>(row.Length);
            foreach (var value in row)
            {
                cells.Add(value!);
            }
            result.Add(cells);
        }
        return result;
    }

    private static BatchGetValuesResponse FullValues() => Values(
        ("'Maps'!A1:Q",
            new[]
            {
                HeaderRow("Maps"),
                Row("Maps", ("map_id", "1"), ("map_name", "Map One"), ("map_filename", "map_one")),
                Row("Maps", ("map_id", "2"), ("map_name", "Map Two"), ("map_filename", "map_two"))
            }),
        ("'NPCs'!A1:BG",
            new[]
            {
                HeaderRow("NPCs"),
                Row("NPCs", ("npc_id", "10"), ("npc_name", "Goose"), ("body_id", "42"))
            }),
        ("'NPC Spawns'!A1:D",
            new[]
            {
                HeaderRow("NPC Spawns"),
                Row("NPC Spawns", ("npc_id", "10"), ("map_id", "1"), ("map_x", "6"), ("map_y", "7"))
            }),
        ("'Warptiles'!A1:F",
            new[]
            {
                HeaderRow("Warptiles"),
                Row("Warptiles", ("map_id", "1"), ("map_x", "2"), ("map_y", "2"), ("warp_id", "2"), ("warp_x", "3"), ("warp_y", "3"))
            }));

    [Fact]
    public async Task ReadGameData_RequestsMetadataFieldsAndQuotedA1RangesForAllFourSheets()
    {
        var operations = new FakeSheetsTransport { Metadata = ValidMetadata(), ValuesResponse = FullValues() };

        await CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None);

        Assert.Equal("sheets.properties(sheetId,title)", operations.GetFields.Single());
        Assert.Equal(
            new[] { "'Maps'!A1:Q", "'NPCs'!A1:BG", "'NPC Spawns'!A1:D", "'Warptiles'!A1:F" },
            operations.BatchGetRanges.Single());
    }

    [Fact]
    public async Task ReadGameData_MapsAllFourSheets_WithDefaultsForOmittedCells()
    {
        var operations = new FakeSheetsTransport { Metadata = ValidMetadata(), ValuesResponse = FullValues() };

        var data = await CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None);

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
        Assert.Equal(42, npc.BodyId);
        Assert.Equal(3, npc.BodyState);
        Assert.Single(data.Spawns);
        Assert.Equal(2, data.Spawns[0].WorksheetRowNumber);
        Assert.Equal(new NpcSpawnRow(10, 1, 5, 6), data.Spawns[0].Value);
        Assert.Single(data.Warps);
        Assert.Equal(2, data.Warps[0].WorksheetRowNumber);
        Assert.Equal(new WarpRow(1, 1, 1, 2, 2, 2), data.Warps[0].Value);
    }

    [Fact]
    public async Task ReadGameData_BlankAndShortRows_PreserveWorksheetRowNumbers()
    {
        var mapsRows = new object?[][]
        {
            HeaderRow("Maps"),
            Row("Maps", ("map_id", "1"), ("map_name", "One"), ("map_filename", "one")),
            new object?[17],
            Truncate(Row("Maps", ("map_id", "4"), ("map_name", "Four"), ("map_filename", "four")), 3)
        };
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            ValuesResponse = Values(
                ("'Maps'!A1:Q", mapsRows),
                ("'NPCs'!A1:BG", new[] { HeaderRow("NPCs") }),
                ("'NPC Spawns'!A1:D", new[] { HeaderRow("NPC Spawns") }),
                ("'Warptiles'!A1:F", new[] { HeaderRow("Warptiles") }))
        };

        var data = await CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                new MapReference(1, "One", "one"),
                new MapReference(4, "Four", "four")
            },
            data.Maps);
    }

    [Fact]
    public async Task ReadOwnedRows_RequestsHeaderOnlyRangesForMapsAndNpcs_AndReturnsOnlyOwnedRows()
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            ValuesResponse = Values(
                ("'Maps'!A1:Q1", new[] { HeaderRow("Maps") }),
                ("'NPCs'!A1:BG1", new[] { HeaderRow("NPCs") }),
                ("'NPC Spawns'!A1:D",
                    new[]
                    {
                        HeaderRow("NPC Spawns"),
                        Row("NPC Spawns", ("npc_id", "10"), ("map_id", "1"), ("map_x", "2"), ("map_y", "2")),
                        new object?[4],
                        Row("NPC Spawns", ("npc_id", "11"), ("map_id", "1"), ("map_x", "3"), ("map_y", "3")),
                        Row("NPC Spawns", ("npc_id", "12"), ("map_id", "2"), ("map_x", "4"), ("map_y", "4"))
                    }),
                ("'Warptiles'!A1:F",
                    new[]
                    {
                        HeaderRow("Warptiles"),
                        Row("Warptiles", ("map_id", "1"), ("map_x", "2"), ("map_y", "2"), ("warp_id", "2"), ("warp_x", "3"), ("warp_y", "3")),
                        Row("Warptiles", ("map_id", "2"), ("map_x", "4"), ("map_y", "4"), ("warp_id", "1"), ("warp_x", "5"), ("warp_y", "5"))
                    }))
        };

        var owned = await CreateGateway(operations).ReadOwnedRowsAsync(SpreadsheetId, 1, CancellationToken.None);

        Assert.Equal(
            new[] { "'Maps'!A1:Q1", "'NPCs'!A1:BG1", "'NPC Spawns'!A1:D", "'Warptiles'!A1:F" },
            operations.BatchGetRanges.Single());
        Assert.Equal(
            new[]
            {
                new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(10, 1, 1, 1)),
                new RemoteRow<NpcSpawnRow>(4, new NpcSpawnRow(11, 1, 2, 2))
            },
            owned.Spawns);
        Assert.Equal(
            new[] { new RemoteRow<WarpRow>(2, new WarpRow(1, 1, 1, 2, 2, 2)) },
            owned.Warps);
    }

    [Fact]
    public async Task ReadMaps_RequestsOnlyTheMapsRange_AndMapsRows()
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            ValuesResponse = Values(
                ("'Maps'!A1:Q",
                    new[]
                    {
                        HeaderRow("Maps"),
                        Row("Maps", ("map_id", "7"), ("map_name", "Seven"), ("map_filename", "seven"))
                    }))
        };

        var maps = await CreateGateway(operations).ReadMapsAsync(SpreadsheetId, CancellationToken.None);

        Assert.Equal(new[] { "'Maps'!A1:Q" }, operations.BatchGetRanges.Single());
        Assert.Equal(new[] { new MapReference(7, "Seven", "seven") }, maps);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadOwnedRows_FailsWhenMapsOrNpcsHeaderDrifted(bool mapsSheet)
    {
        var mapsHeader = HeaderRow("Maps");
        var npcsHeader = HeaderRow("NPCs");
        if (mapsSheet)
        {
            (mapsHeader[0], mapsHeader[1]) = (mapsHeader[1], mapsHeader[0]);
        }
        else
        {
            npcsHeader = npcsHeader.Take(npcsHeader.Length - 1).ToArray();
        }
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            ValuesResponse = Values(
                ("'Maps'!A1:Q1", new[] { mapsHeader }),
                ("'NPCs'!A1:BG1", new[] { npcsHeader }),
                ("'NPC Spawns'!A1:D", new[] { HeaderRow("NPC Spawns") }),
                ("'Warptiles'!A1:F", new[] { HeaderRow("Warptiles") }))
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadOwnedRowsAsync(SpreadsheetId, 1, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.Equal(SpreadsheetId, exception.SpreadsheetId);
        Assert.False(exception.RequestWasRejected);
    }

    [Fact]
    public async Task ReadGameData_ReorderedHeader_FailsBeforeMapping()
    {
        var warptilesHeader = HeaderRow("Warptiles");
        (warptilesHeader[1], warptilesHeader[2]) = (warptilesHeader[2], warptilesHeader[1]);
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            ValuesResponse = Values(
                ("'Maps'!A1:Q", new[] { HeaderRow("Maps") }),
                ("'NPCs'!A1:BG", new[] { HeaderRow("NPCs") }),
                ("'NPC Spawns'!A1:D", new[] { HeaderRow("NPC Spawns") }),
                ("'Warptiles'!A1:F", new[] { warptilesHeader, Row("Warptiles", ("map_id", "1"), ("map_x", "1"), ("map_y", "1"), ("warp_id", "2"), ("warp_x", "2"), ("warp_y", "2")) }))
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.False(exception.RequestWasRejected);
        Assert.Contains("Warptiles", exception.Message);
    }

    [Theory]
    [InlineData("Maps")]
    [InlineData("NPCs")]
    [InlineData("NPC Spawns")]
    [InlineData("Warptiles")]
    public async Task ReadGameData_MissingRequiredWorksheet_FailsAsSchema(string missingSheet)
    {
        var sheets = ValidMetadata().Sheets.Where(sheet => sheet.Properties.Title != missingSheet).ToList();
        var operations = new FakeSheetsTransport
        {
            Metadata = new Spreadsheet { SpreadsheetId = SpreadsheetId, Sheets = sheets },
            ValuesResponse = FullValues()
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.False(exception.RequestWasRejected);
        Assert.Contains(missingSheet, exception.Message);
    }

    [Fact]
    public async Task ReadGameData_DuplicateWorksheetTitle_FailsAsSchema()
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = Metadata(
                ("Maps", 0), ("Maps", 1), ("NPCs", 2), ("NPC Spawns", 3), ("Warptiles", 4)),
            ValuesResponse = FullValues()
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.False(exception.RequestWasRejected);
        Assert.Contains("Maps", exception.Message);
    }

    [Fact]
    public async Task ReadGameData_MissingValueRange_FailsAsSchema()
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            ValuesResponse = new BatchGetValuesResponse
            {
                ValueRanges = new List<ValueRange>
                {
                    new() { Range = "'Maps'!A1:Q", Values = ToILists(new[] { HeaderRow("Maps") }) },
                    new() { Range = "'NPCs'!A1:BG" },
                    new() { Range = "'NPC Spawns'!A1:D", Values = ToILists(new[] { HeaderRow("NPC Spawns") }) },
                    new() { Range = "'Warptiles'!A1:F", Values = ToILists(new[] { HeaderRow("Warptiles") }) }
                }
            }
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.False(exception.RequestWasRejected);
        Assert.Contains("NPCs", exception.Message);
    }

    [Fact]
    public async Task ReadGameData_WrongValueRangeCount_FailsAsSchema()
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            ValuesResponse = Values(
                ("'Maps'!A1:Q", new[] { HeaderRow("Maps") }),
                ("'NPCs'!A1:BG", new[] { HeaderRow("NPCs") }))
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.False(exception.RequestWasRejected);
    }

    [Fact]
    public async Task ReadGameData_MalformedRow_FailsAsSchema()
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            ValuesResponse = Values(
                ("'Maps'!A1:Q", new[] { HeaderRow("Maps") }),
                ("'NPCs'!A1:BG", new[] { HeaderRow("NPCs") }),
                ("'NPC Spawns'!A1:D",
                    new[]
                    {
                        HeaderRow("NPC Spawns"),
                        Row("NPC Spawns", ("npc_id", "10"), ("map_id", "1"), ("map_x", "not-a-number"), ("map_y", "6"))
                    }),
                ("'Warptiles'!A1:F", new[] { HeaderRow("Warptiles") }))
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.False(exception.RequestWasRejected);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task ReadGameData_DuplicateMapId_FailsAsSchema()
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            ValuesResponse = Values(
                ("'Maps'!A1:Q",
                    new[]
                    {
                        HeaderRow("Maps"),
                        Row("Maps", ("map_id", "1"), ("map_name", "One"), ("map_filename", "one")),
                        Row("Maps", ("map_id", "1"), ("map_name", "Again"), ("map_filename", "again"))
                    }),
                ("'NPCs'!A1:BG", new[] { HeaderRow("NPCs") }),
                ("'NPC Spawns'!A1:D", new[] { HeaderRow("NPC Spawns") }),
                ("'Warptiles'!A1:F", new[] { HeaderRow("Warptiles") }))
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.False(exception.RequestWasRejected);
        Assert.Contains("map id", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadGameData_DuplicateNpcId_FailsAsSchema()
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            ValuesResponse = Values(
                ("'Maps'!A1:Q", new[] { HeaderRow("Maps") }),
                ("'NPCs'!A1:BG",
                    new[]
                    {
                        HeaderRow("NPCs"),
                        Row("NPCs", ("npc_id", "10"), ("npc_name", "Goose")),
                        Row("NPCs", ("npc_id", "10"), ("npc_name", "Goose Again"))
                    }),
                ("'NPC Spawns'!A1:D", new[] { HeaderRow("NPC Spawns") }),
                ("'Warptiles'!A1:F", new[] { HeaderRow("Warptiles") }))
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.False(exception.RequestWasRejected);
        Assert.Contains("npc id", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(401, GatewayFailureKind.Authentication, false)]
    [InlineData(403, GatewayFailureKind.Permission, false)]
    [InlineData(404, GatewayFailureKind.NotFound, false)]
    [InlineData(429, GatewayFailureKind.RateLimited, true)]
    [InlineData(500, GatewayFailureKind.Temporary, false)]
    [InlineData(503, GatewayFailureKind.Temporary, false)]
    public async Task ReadGameData_ApiFailure_MapsStatusToFailureKind(int status, GatewayFailureKind kind, bool rejected)
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            GetError = new GoogleApiException("api failure") { HttpStatusCode = (HttpStatusCode)status }
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadMapsAsync(SpreadsheetId, CancellationToken.None));

        Assert.Equal(kind, exception.Kind);
        Assert.Equal(SpreadsheetId, exception.SpreadsheetId);
        Assert.Equal(rejected, exception.RequestWasRejected);
        Assert.IsType<GoogleApiException>(exception.InnerException);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(GoogleApiException))]
    public async Task ReadGameData_TransportFailure_MapsToTransportWithNoRejection(Type exceptionType)
    {
        Exception error = exceptionType == typeof(IOException)
            ? new IOException("socket failure")
            : exceptionType == typeof(GoogleApiException)
                ? new GoogleApiException("no response")
                : new HttpRequestException("connection refused");
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            GetError = error
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadMapsAsync(SpreadsheetId, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Transport, exception.Kind);
        Assert.False(exception.RequestWasRejected);
    }

    [Fact]
    public async Task ReadGameData_CallerCancellation_PropagatesUnwrapped()
    {
        var operations = new FakeSheetsTransport { Metadata = ValidMetadata(), ValuesResponse = FullValues() };
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateGateway(operations).ReadGameDataAsync(SpreadsheetId, 1, cancellationTokenSource.Token));

        Assert.IsNotType<GameDataGatewayException>(exception);
        Assert.Empty(operations.BatchGetRanges);
    }

    [Fact]
    public async Task ReadOwnedRows_MidCallCancellation_PropagatesUnwrapped()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            BatchGetError = new TaskCanceledException("canceled", null, cancellationTokenSource.Token)
        };

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateGateway(operations).ReadOwnedRowsAsync(SpreadsheetId, 1, cancellationTokenSource.Token));

        Assert.IsNotType<GameDataGatewayException>(exception);
    }

    [Fact]
    public async Task ReplaceOwnedRows_SendsSingleBatchUpdate_UsingMetadataSheetIds()
    {
        var operations = new FakeSheetsTransport { Metadata = ValidMetadata() };
        var spawnPlan = new ReplacementPlan(
            "NPC Spawns",
            new[] { new RowDelete(4), new RowDelete(2) },
            new[] { new RowInsert(new string?[] { "10", "1", "5", "6" }) });
        var warpPlan = new ReplacementPlan(
            "Warptiles",
            new[] { new RowDelete(3) },
            new[] { new RowInsert(new string?[] { "1", "2", "3", "9", "4", "5" }) });

        await CreateGateway(operations).ReplaceOwnedRowsAsync(SpreadsheetId, spawnPlan, warpPlan, CancellationToken.None);

        Assert.Equal("sheets.properties(sheetId,title)", operations.GetFields.Single());
        var request = Assert.Single(operations.BatchUpdates);
        Assert.Equal(5, request.Requests.Count);
        Assert.Equal(2, request.Requests[0].DeleteDimension.Range.SheetId);
        Assert.Equal(3, request.Requests[0].DeleteDimension.Range.StartIndex);
        Assert.Equal(1, request.Requests[1].DeleteDimension.Range.StartIndex);
        Assert.Equal(2, request.Requests[2].AppendCells.SheetId);
        Assert.Equal(3, request.Requests[3].DeleteDimension.Range.SheetId);
        Assert.Equal(3, request.Requests[4].AppendCells.SheetId);
        Assert.Equal("userEnteredValue", request.Requests[2].AppendCells.Fields);
    }

    [Fact]
    public async Task ReplaceOwnedRows_EmptyPlans_MakesNoBatchUpdateCall()
    {
        var operations = new FakeSheetsTransport { Metadata = ValidMetadata() };

        await CreateGateway(operations).ReplaceOwnedRowsAsync(
            SpreadsheetId,
            new ReplacementPlan("NPC Spawns", Array.Empty<RowDelete>(), Array.Empty<RowInsert>()),
            new ReplacementPlan("Warptiles", Array.Empty<RowDelete>(), Array.Empty<RowInsert>()),
            CancellationToken.None);

        Assert.Empty(operations.BatchUpdates);
    }

    [Fact]
    public async Task ReplaceOwnedRows_RateLimitedBatchUpdate_MapsToRateLimitedRejection()
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            BatchUpdateError = new GoogleApiException("rate limited") { HttpStatusCode = HttpStatusCode.TooManyRequests }
        };
        var spawnPlan = new ReplacementPlan(
            "NPC Spawns",
            new[] { new RowDelete(2) },
            Array.Empty<RowInsert>());

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReplaceOwnedRowsAsync(
                SpreadsheetId, spawnPlan,
                new ReplacementPlan("Warptiles", Array.Empty<RowDelete>(), Array.Empty<RowInsert>()),
                CancellationToken.None));

        Assert.Equal(GatewayFailureKind.RateLimited, exception.Kind);
        Assert.True(exception.RequestWasRejected);
    }

    [Fact]
    public async Task ReplaceOwnedRows_NonMutableSheetPlan_FailsAsSchemaWithoutSendingAnything()
    {
        var operations = new FakeSheetsTransport { Metadata = ValidMetadata() };
        var badPlan = new ReplacementPlan(
            "Maps",
            new[] { new RowDelete(2) },
            Array.Empty<RowInsert>());

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReplaceOwnedRowsAsync(
                SpreadsheetId, badPlan,
                new ReplacementPlan("Warptiles", Array.Empty<RowDelete>(), Array.Empty<RowInsert>()),
                CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.Equal(SpreadsheetId, exception.SpreadsheetId);
        Assert.False(exception.RequestWasRejected);
        Assert.Empty(operations.GetFields);
        Assert.Empty(operations.BatchUpdates);
    }

    [Fact]
    public async Task ReplaceOwnedRows_DuplicateDeleteRowNumbers_FailsAsSchemaWithoutSendingAnything()
    {
        var operations = new FakeSheetsTransport { Metadata = ValidMetadata() };
        var spawnPlan = new ReplacementPlan(
            "NPC Spawns",
            new[] { new RowDelete(3), new RowDelete(3) },
            new[] { new RowInsert(new string?[] { "10", "1", "5", "6" }) });

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReplaceOwnedRowsAsync(
                SpreadsheetId, spawnPlan,
                new ReplacementPlan("Warptiles", Array.Empty<RowDelete>(), Array.Empty<RowInsert>()),
                CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, exception.Kind);
        Assert.Equal(SpreadsheetId, exception.SpreadsheetId);
        Assert.False(exception.RequestWasRejected);
        Assert.Empty(operations.GetFields);
        Assert.Empty(operations.BatchUpdates);
    }

    [Fact]
    public async Task ReadMaps_CancellationNotTiedToCallerToken_MapsToTransport()
    {
        var operations = new FakeSheetsTransport
        {
            Metadata = ValidMetadata(),
            GetError = new TaskCanceledException("internal timeout")
        };

        var exception = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => CreateGateway(operations).ReadMapsAsync(SpreadsheetId, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Transport, exception.Kind);
        Assert.False(exception.RequestWasRejected);
        Assert.IsType<TaskCanceledException>(exception.InnerException);
    }

    internal sealed class FakeSheetsTransport : IGoogleSheetsOperations
    {
        public List<string> GetFields { get; } = new();

        public List<IReadOnlyList<string>> BatchGetRanges { get; } = new();

        public List<BatchUpdateSpreadsheetRequest> BatchUpdates { get; } = new();

        public Spreadsheet? Metadata { get; set; }

        public BatchGetValuesResponse? ValuesResponse { get; set; }

        public Exception? GetError { get; set; }

        public Exception? BatchGetError { get; set; }

        public Exception? BatchUpdateError { get; set; }

        public Task<Spreadsheet> GetSpreadsheetAsync(string spreadsheetId, string fields, CancellationToken cancellationToken)
        {
            GetFields.Add(fields);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromException<Spreadsheet>(new TaskCanceledException("Operation canceled.", null, cancellationToken));
            }
            if (GetError is not null)
            {
                return Task.FromException<Spreadsheet>(GetError);
            }
            return Task.FromResult(Metadata ?? throw new InvalidOperationException("Metadata was not configured."));
        }

        public Task<BatchGetValuesResponse> BatchGetValuesAsync(
            string spreadsheetId,
            IReadOnlyList<string> ranges,
            CancellationToken cancellationToken)
        {
            BatchGetRanges.Add(ranges.ToList().AsReadOnly());
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromException<BatchGetValuesResponse>(new TaskCanceledException("Operation canceled.", null, cancellationToken));
            }
            if (BatchGetError is not null)
            {
                return Task.FromException<BatchGetValuesResponse>(BatchGetError);
            }
            return Task.FromResult(ValuesResponse ?? throw new InvalidOperationException("ValuesResponse was not configured."));
        }

        public Task<BatchUpdateSpreadsheetResponse> BatchUpdateAsync(
            string spreadsheetId,
            BatchUpdateSpreadsheetRequest request,
            CancellationToken cancellationToken)
        {
            BatchUpdates.Add(request);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromException<BatchUpdateSpreadsheetResponse>(new TaskCanceledException("Operation canceled.", null, cancellationToken));
            }
            if (BatchUpdateError is not null)
            {
                return Task.FromException<BatchUpdateSpreadsheetResponse>(BatchUpdateError);
            }
            return Task.FromResult(new BatchUpdateSpreadsheetResponse { SpreadsheetId = spreadsheetId });
        }
    }
}
