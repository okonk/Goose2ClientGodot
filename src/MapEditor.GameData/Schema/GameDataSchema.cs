using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapEditor.GameData.Schema;

public sealed class GameDataSchema
{
    private const string EmbeddedResourceName = "MapEditor.GameData.game-data-schema.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    private readonly Dictionary<string, SheetSchema> _sheetsByName;

    internal GameDataSchema(IReadOnlyList<SheetSchema> sheets, Dictionary<string, SheetSchema> sheetsByName)
    {
        Sheets = sheets;
        _sheetsByName = sheetsByName;
    }

    public IReadOnlyList<SheetSchema> Sheets { get; }

    public static GameDataSchema LoadEmbedded()
    {
        var assembly = typeof(GameDataSchema).Assembly;
        var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedResourceName}' was not found in {assembly.FullName}.");
        using (stream)
        {
            return Load(stream);
        }
    }

    public static GameDataSchema Load(Stream stream)
    {
        SchemaDocumentDto? document;
        try
        {
            document = JsonSerializer.Deserialize<SchemaDocumentDto>(stream, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Game data schema is not valid JSON.", ex);
        }

        if (document?.Sheets is not { Count: > 0 } sheets)
        {
            throw new InvalidDataException("Game data schema is missing a non-empty 'sheets' array.");
        }

        var parsed = new List<SheetSchema>(sheets.Count);
        var byName = new Dictionary<string, SheetSchema>(StringComparer.Ordinal);
        foreach (var sheetDto in sheets)
        {
            if (string.IsNullOrWhiteSpace(sheetDto.Sheet))
            {
                throw new InvalidDataException("Game data schema contains a sheet with a missing or blank name.");
            }
            if (string.IsNullOrWhiteSpace(sheetDto.Table))
            {
                throw new InvalidDataException(
                    $"Sheet '{sheetDto.Sheet}' has a missing or blank 'table'.");
            }
            var columns = ParseColumns(sheetDto);
            var schema = new SheetSchema(sheetDto.Sheet, sheetDto.Table, columns);
            if (!byName.TryAdd(schema.Sheet, schema))
            {
                throw new InvalidDataException(
                    $"Game data schema contains duplicate sheet '{schema.Sheet}'.");
            }
            parsed.Add(schema);
        }

        return new GameDataSchema(parsed.AsReadOnly(), byName);
    }

    public SheetSchema GetRequiredSheet(string sheetName)
    {
        if (!_sheetsByName.TryGetValue(sheetName, out var sheet))
        {
            throw new KeyNotFoundException($"Sheet '{sheetName}' was not found in the game data schema.");
        }
        return sheet;
    }

    private static IReadOnlyList<ColumnSchema> ParseColumns(SheetDto sheetDto)
    {
        if (sheetDto.Columns is not { Count: > 0 } columnDtos)
        {
            throw new InvalidDataException(
                $"Sheet '{sheetDto.Sheet}' has a missing or empty 'columns' array.");
        }

        var columns = new List<ColumnSchema>(columnDtos.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dto in columnDtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                throw new InvalidDataException(
                    $"Sheet '{sheetDto.Sheet}' contains a column with a missing or blank name.");
            }
            if (!seen.Add(dto.Name))
            {
                throw new InvalidDataException(
                    $"Sheet '{sheetDto.Sheet}' contains duplicate column '{dto.Name}'.");
            }
            if (string.IsNullOrWhiteSpace(dto.Header))
            {
                throw new InvalidDataException(
                    $"Column '{dto.Name}' on sheet '{sheetDto.Sheet}' has a missing or blank header.");
            }
            if (string.IsNullOrWhiteSpace(dto.Kind) || string.IsNullOrWhiteSpace(dto.Sql))
            {
                throw new InvalidDataException(
                    $"Column '{dto.Name}' on sheet '{sheetDto.Sheet}' has a missing 'kind' or 'sql'.");
            }
            columns.Add(new ColumnSchema(dto.Name, dto.Header, dto.Kind, dto.Sql, dto.Default,
                dto.Required, dto.Pk, dto.Ref));
        }

        return columns.AsReadOnly();
    }

    private sealed class SchemaDocumentDto
    {
        public List<SheetDto>? Sheets { get; set; }
    }

    private sealed class SheetDto
    {
        public string? Sheet { get; set; }
        public string? Table { get; set; }
        public List<ColumnDto>? Columns { get; set; }
    }

    private sealed class ColumnDto
    {
        public string? Name { get; set; }
        public string? Header { get; set; }
        public string? Kind { get; set; }
        public string? Sql { get; set; }
        public string? Default { get; set; }
        public bool Required { get; set; }
        public bool Pk { get; set; }
        public string? Ref { get; set; }
    }
}

public sealed class SheetSchema
{
    private readonly Dictionary<string, int> _columnIndices;

    internal SheetSchema(string sheet, string table, IReadOnlyList<ColumnSchema> columns)
    {
        Sheet = sheet;
        Table = table;
        Columns = columns;
        _columnIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            _columnIndices.Add(columns[i].Name, i);
        }
    }

    public string Sheet { get; }
    public string Table { get; }
    public IReadOnlyList<ColumnSchema> Columns { get; }

    public ColumnSchema GetRequiredColumn(string columnName)
    {
        return Columns[GetColumnIndex(columnName)];
    }

    public int GetColumnIndex(string columnName)
    {
        if (!_columnIndices.TryGetValue(columnName, out var index))
        {
            throw new KeyNotFoundException($"Column '{columnName}' was not found on sheet '{Sheet}'.");
        }
        return index;
    }
}

public sealed class ColumnSchema
{
    internal ColumnSchema(string name, string header, string kind, string sql, string? def,
        bool required, bool isPrimaryKey, string? refSheet)
    {
        Name = name;
        Header = header;
        Kind = kind;
        Sql = sql;
        Default = def;
        Required = required;
        IsPrimaryKey = isPrimaryKey;
        RefSheet = refSheet;
    }

    public string Name { get; }
    public string Header { get; }
    public string Kind { get; }
    public string Sql { get; }
    public string? Default { get; }
    public bool Required { get; }
    public bool IsPrimaryKey { get; }
    public string? RefSheet { get; }
}
