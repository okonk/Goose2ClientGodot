using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Util;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Schema;

using BatchGetRequest = Google.Apis.Sheets.v4.SpreadsheetsResource.ValuesResource.BatchGetRequest;

namespace MapEditor.GameData.Google.Sheets;

internal interface IGoogleSheetsOperations
{
    Task<Spreadsheet> GetSpreadsheetAsync(string spreadsheetId, string fields, CancellationToken cancellationToken);

    Task<BatchGetValuesResponse> BatchGetValuesAsync(
        string spreadsheetId,
        IReadOnlyList<string> ranges,
        CancellationToken cancellationToken);

    Task<BatchUpdateSpreadsheetResponse> BatchUpdateAsync(
        string spreadsheetId,
        BatchUpdateSpreadsheetRequest request,
        CancellationToken cancellationToken);
}

internal sealed class GoogleSheetsOperations : IGoogleSheetsOperations
{
    private readonly SheetsService _service;

    public GoogleSheetsOperations(SheetsService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public Task<Spreadsheet> GetSpreadsheetAsync(string spreadsheetId, string fields, CancellationToken cancellationToken)
    {
        var request = _service.Spreadsheets.Get(spreadsheetId);
        request.Fields = fields;
        return request.ExecuteAsync(cancellationToken);
    }

    public Task<BatchGetValuesResponse> BatchGetValuesAsync(
        string spreadsheetId,
        IReadOnlyList<string> ranges,
        CancellationToken cancellationToken)
    {
        var request = _service.Spreadsheets.Values.BatchGet(spreadsheetId);
        request.Ranges = new Repeatable<string>(ranges.ToArray());
        request.MajorDimension = BatchGetRequest.MajorDimensionEnum.ROWS;
        request.ValueRenderOption = BatchGetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
        return request.ExecuteAsync(cancellationToken);
    }

    public Task<BatchUpdateSpreadsheetResponse> BatchUpdateAsync(
        string spreadsheetId,
        BatchUpdateSpreadsheetRequest request,
        CancellationToken cancellationToken)
        => _service.Spreadsheets.BatchUpdate(request, spreadsheetId)
            .ExecuteAsync(cancellationToken);
}

public sealed class GoogleSheetsGateway : IGameDataGateway
{
    private const string MetadataFields = "sheets.properties(sheetId,title)";
    private const string Maps = "Maps";
    private const string Npcs = "NPCs";
    private const string NpcSpawns = "NPC Spawns";
    private const string Warptiles = "Warptiles";

    private readonly IGoogleSheetsOperations _operations;
    private readonly GameDataSchema _schema;
    private readonly SheetRowMapper _mapper;

    internal GoogleSheetsGateway(IGoogleSheetsOperations operations, GameDataSchema schema)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _mapper = new SheetRowMapper(_schema);
    }

    public GoogleSheetsGateway(SheetsService service, GameDataSchema? schema = null)
        : this(new GoogleSheetsOperations(service), schema ?? GameDataSchema.LoadEmbedded())
    {
    }

    public async Task<IReadOnlyList<MapReference>> ReadMapsAsync(
        string spreadsheetId,
        CancellationToken cancellationToken)
    {
        try
        {
            var read = (await ReadSheetsAsync(
                spreadsheetId,
                new[] { (Schema: _schema.GetRequiredSheet(Maps), LastRow: (int?)null) },
                cancellationToken).ConfigureAwait(false))[0];
            var maps = new List<MapReference>(read.DataRows.Count);
            var mapIds = new HashSet<int>();
            foreach (var (_, cells) in read.DataRows)
            {
                var map = _mapper.MapMap(cells);
                if (!mapIds.Add(map.MapId))
                {
                    throw SchemaFailure(spreadsheetId,
                        $"Worksheet '{Maps}' contains duplicate map id {map.MapId}.");
                }
                maps.Add(map);
            }
            return maps;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GameDataGatewayException)
        {
            throw;
        }
        catch (FormatException ex)
        {
            throw new GameDataGatewayException(GatewayFailureKind.Schema, spreadsheetId, false, ex.Message, ex);
        }
        catch (Exception ex)
        {
            throw GoogleFailureMapper.Map(ex, spreadsheetId);
        }
    }

    public async Task<RemoteGameData> ReadGameDataAsync(
        string spreadsheetId,
        int mapId,
        CancellationToken cancellationToken)
    {
        try
        {
            var reads = await ReadSheetsAsync(spreadsheetId, AllSheetsFull(), cancellationToken).ConfigureAwait(false);
            var maps = new List<MapReference>();
            var mapIds = new HashSet<int>();
            var npcs = new Dictionary<int, NpcAppearance>();
            var spawns = new List<RemoteRow<NpcSpawnRow>>();
            var warps = new List<RemoteRow<WarpRow>>();
            foreach (var read in reads)
            {
                switch (read.Schema.Sheet)
                {
                    case Maps:
                        foreach (var (_, cells) in read.DataRows)
                        {
                            var map = _mapper.MapMap(cells);
                            if (!mapIds.Add(map.MapId))
                            {
                                throw SchemaFailure(spreadsheetId,
                                    $"Worksheet '{Maps}' contains duplicate map id {map.MapId}.");
                            }
                            maps.Add(map);
                        }
                        break;
                    case Npcs:
                        foreach (var (_, cells) in read.DataRows)
                        {
                            var npc = _mapper.MapNpc(cells);
                            if (!npcs.TryAdd(npc.NpcId, npc))
                            {
                                throw SchemaFailure(spreadsheetId,
                                    $"Worksheet '{Npcs}' contains duplicate npc id {npc.NpcId}.");
                            }
                        }
                        break;
                    case NpcSpawns:
                        foreach (var (row, cells) in read.DataRows)
                        {
                            spawns.Add(new RemoteRow<NpcSpawnRow>(row, _mapper.MapSpawn(cells)));
                        }
                        break;
                    case Warptiles:
                        foreach (var (row, cells) in read.DataRows)
                        {
                            warps.Add(new RemoteRow<WarpRow>(row, _mapper.MapWarp(cells)));
                        }
                        break;
                }
            }
            return new RemoteGameData(maps, npcs, spawns, warps);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GameDataGatewayException)
        {
            throw;
        }
        catch (FormatException ex)
        {
            throw new GameDataGatewayException(GatewayFailureKind.Schema, spreadsheetId, false, ex.Message, ex);
        }
        catch (Exception ex)
        {
            throw GoogleFailureMapper.Map(ex, spreadsheetId);
        }
    }

    public async Task<RemoteOwnedRows> ReadOwnedRowsAsync(
        string spreadsheetId,
        int mapId,
        CancellationToken cancellationToken)
    {
        try
        {
            var reads = await ReadSheetsAsync(spreadsheetId, PreflightSheets(), cancellationToken).ConfigureAwait(false);
            var spawns = new List<RemoteRow<NpcSpawnRow>>();
            var warps = new List<RemoteRow<WarpRow>>();
            foreach (var read in reads)
            {
                switch (read.Schema.Sheet)
                {
                    case NpcSpawns:
                        foreach (var (row, cells) in read.DataRows)
                        {
                            var spawn = _mapper.MapSpawn(cells);
                            if (spawn.MapId == mapId)
                            {
                                spawns.Add(new RemoteRow<NpcSpawnRow>(row, spawn));
                            }
                        }
                        break;
                    case Warptiles:
                        foreach (var (row, cells) in read.DataRows)
                        {
                            var warp = _mapper.MapWarp(cells);
                            if (warp.MapId == mapId)
                            {
                                warps.Add(new RemoteRow<WarpRow>(row, warp));
                            }
                        }
                        break;
                }
            }
            return new RemoteOwnedRows(spawns, warps);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GameDataGatewayException)
        {
            throw;
        }
        catch (FormatException ex)
        {
            throw new GameDataGatewayException(GatewayFailureKind.Schema, spreadsheetId, false, ex.Message, ex);
        }
        catch (Exception ex)
        {
            throw GoogleFailureMapper.Map(ex, spreadsheetId);
        }
    }

    public async Task ReplaceOwnedRowsAsync(
        string spreadsheetId,
        ReplacementPlan spawnPlan,
        ReplacementPlan warpPlan,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidatePlans(spreadsheetId, spawnPlan, warpPlan);
            if (spawnPlan.Deletes.Count + spawnPlan.Inserts.Count + warpPlan.Deletes.Count + warpPlan.Inserts.Count == 0)
            {
                return;
            }
            var sheetIds = await ResolveSheetIdsAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
            var request = GoogleBatchBuilder.Build(sheetIds, spawnPlan, warpPlan);
            await _operations.BatchUpdateAsync(spreadsheetId, request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GameDataGatewayException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw GoogleFailureMapper.Map(ex, spreadsheetId);
        }
    }

    // Only the two mutable worksheets may ever receive delete/append requests.
    private static void ValidatePlans(string spreadsheetId, ReplacementPlan spawnPlan, ReplacementPlan warpPlan)
    {
        foreach (var plan in new[] { spawnPlan, warpPlan })
        {
            if (plan.Sheet != NpcSpawns && plan.Sheet != Warptiles)
            {
                throw SchemaFailure(spreadsheetId, $"Replacement plan targets non-mutable worksheet '{plan.Sheet}'.");
            }
            var seen = new HashSet<int>();
            foreach (var delete in plan.Deletes)
            {
                if (!seen.Add(delete.WorksheetRowNumber))
                {
                    throw SchemaFailure(spreadsheetId,
                        $"Replacement plan for '{plan.Sheet}' contains duplicate delete row {delete.WorksheetRowNumber}.");
                }
            }
        }
    }

    private (SheetSchema Schema, int? LastRow)[] AllSheetsFull() =>
    [
        (_schema.GetRequiredSheet(Maps), null),
        (_schema.GetRequiredSheet(Npcs), null),
        (_schema.GetRequiredSheet(NpcSpawns), null),
        (_schema.GetRequiredSheet(Warptiles), null)
    ];

    private (SheetSchema Schema, int? LastRow)[] PreflightSheets() =>
    [
        (_schema.GetRequiredSheet(Maps), 1),
        (_schema.GetRequiredSheet(Npcs), 1),
        (_schema.GetRequiredSheet(NpcSpawns), null),
        (_schema.GetRequiredSheet(Warptiles), null)
    ];

    private async Task<Dictionary<string, int>> ResolveSheetIdsAsync(
        string spreadsheetId,
        CancellationToken cancellationToken)
    {
        var metadata = await _operations
            .GetSpreadsheetAsync(spreadsheetId, MetadataFields, cancellationToken)
            .ConfigureAwait(false);
        var sheetIds = new Dictionary<string, int>(StringComparer.Ordinal);
        if (metadata.Sheets is not null)
        {
            foreach (var sheet in metadata.Sheets)
            {
                var properties = sheet.Properties;
                if (properties?.Title is null || properties.SheetId is null)
                {
                    continue;
                }
                if (!sheetIds.TryAdd(properties.Title, properties.SheetId.Value))
                {
                    throw SchemaFailure(spreadsheetId,
                        $"Spreadsheet contains duplicate worksheet '{properties.Title}'.");
                }
            }
        }
        foreach (var title in new[] { Maps, Npcs, NpcSpawns, Warptiles })
        {
            if (!sheetIds.ContainsKey(title))
            {
                throw SchemaFailure(spreadsheetId, $"Spreadsheet is missing required worksheet '{title}'.");
            }
        }
        return sheetIds;
    }

    private async Task<IReadOnlyList<SheetRead>> ReadSheetsAsync(
        string spreadsheetId,
        (SheetSchema Schema, int? LastRow)[] targets,
        CancellationToken cancellationToken)
    {
        await ResolveSheetIdsAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
        var ranges = targets
            .Select(target => BuildRange(target.Schema.Sheet, target.Schema.Columns.Count, target.LastRow))
            .ToList();
        var response = await _operations
            .BatchGetValuesAsync(spreadsheetId, ranges, cancellationToken)
            .ConfigureAwait(false);
        var valueRanges = response?.ValueRanges;
        if (valueRanges is null || valueRanges.Count != targets.Length)
        {
            throw SchemaFailure(spreadsheetId,
                "Values response did not return one value range per requested range.");
        }

        var reads = new List<SheetRead>(targets.Length);
        for (var i = 0; i < targets.Length; i++)
        {
            var schema = targets[i].Schema;
            var values = valueRanges[i]?.Values;
            if (values is null || values.Count == 0)
            {
                throw SchemaFailure(spreadsheetId,
                    $"Worksheet '{schema.Sheet}' returned no rows; expected a header on row 1.");
            }
            var rows = new List<(int Row, string?[] Cells)>(values.Count - 1);
            for (var rowIndex = 1; rowIndex < values.Count; rowIndex++)
            {
                var cells = ToCells(values[rowIndex], schema.Columns.Count);
                if (cells.All(cell => string.IsNullOrWhiteSpace(cell)))
                {
                    continue;
                }
                rows.Add((rowIndex + 1, cells));
            }
            reads.Add(new SheetRead(schema, ToCells(values[0], schema.Columns.Count), rows));
        }

        foreach (var read in reads)
        {
            var mismatches = SchemaHeaderValidator.Validate(read.Schema, read.Header);
            if (mismatches.Count > 0)
            {
                var detail = string.Join("; ", mismatches
                    .Take(3)
                    .Select(m => $"column {m.ColumnIndex + 1} expected '{m.Expected}' but was '{m.Actual}'"));
                throw SchemaFailure(spreadsheetId,
                    $"Worksheet '{read.Schema.Sheet}' header does not match the schema: {detail}.");
            }
        }
        return reads;
    }

    // Ranges start at row 1 so response row i maps to worksheet row i + 1.
    private static string BuildRange(string title, int columnCount, int? lastRow)
    {
        var range = $"'{title}'!A1:{ToColumnLetter(columnCount)}";
        return lastRow is null ? range : $"{range}{lastRow.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string ToColumnLetter(int oneBasedColumn)
    {
        var result = string.Empty;
        while (oneBasedColumn > 0)
        {
            var remainder = (oneBasedColumn - 1) % 26;
            result = (char)('A' + remainder) + result;
            oneBasedColumn = (oneBasedColumn - 1) / 26;
        }
        return result;
    }

    private static string?[] ToCells(IList<object> row, int width)
    {
        var cells = new string?[width];
        for (var i = 0; i < width; i++)
        {
            var value = i < row.Count ? row[i] : null;
            cells[i] = value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        return cells;
    }

    private static GameDataGatewayException SchemaFailure(string spreadsheetId, string message)
        => new(GatewayFailureKind.Schema, spreadsheetId, false, message);

    private sealed record SheetRead(SheetSchema Schema, string?[] Header, IReadOnlyList<(int Row, string?[] Cells)> DataRows);
}
