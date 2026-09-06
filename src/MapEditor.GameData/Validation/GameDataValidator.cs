using System;
using System.Collections.Generic;
using System.IO;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Schema;

namespace MapEditor.GameData.Validation;

public sealed class GameDataValidator
{
    private const string WarptilesSheet = "Warptiles";
    private const string WarpXColumn = "warp_x";
    private const string WarpYColumn = "warp_y";

    private readonly int _warpXMax;
    private readonly int _warpYMax;

    public GameDataValidator(GameDataSchema schema)
    {
        var sheet = schema.GetRequiredSheet(WarptilesSheet);
        _warpXMax = ResolveRowRange(WarpXColumn, sheet.GetRequiredColumn(WarpXColumn).Sql).Max;
        _warpYMax = ResolveRowRange(WarpYColumn, sheet.GetRequiredColumn(WarpYColumn).Sql).Max;
    }

    public ValidationResult ValidateSpawns(
        IEnumerable<NpcSpawnRow> rows,
        IReadOnlyDictionary<int, NpcAppearance> npcs,
        MapDimensions currentMapDimensions)
    {
        RequirePositiveDimensions(currentMapDimensions);

        var errors = new List<ValidationIssue>();
        foreach (var row in rows)
        {
            if (!npcs.ContainsKey(row.NpcId))
            {
                errors.Add(new ValidationIssue(ValidationCodes.SpawnNpcNotFound,
                    $"NPC '{row.NpcId}' referenced by spawn at ({row.MapX}, {row.MapY}) was not found."));
            }
            if (!IsWithin(row.MapX, row.MapY, currentMapDimensions))
            {
                errors.Add(new ValidationIssue(ValidationCodes.SpawnOutOfBounds,
                    $"Spawn at ({row.MapX}, {row.MapY}) is outside the current map bounds " +
                    $"(0..{currentMapDimensions.Width - 1}, 0..{currentMapDimensions.Height - 1})."));
            }
        }

        return new ValidationResult(errors.AsReadOnly());
    }

    public ValidationResult ValidateWarps(
        IEnumerable<WarpRow> rows,
        IReadOnlyDictionary<int, MapReference> maps,
        IReadOnlyDictionary<int, MapDimensions> openMapDimensions,
        MapDimensions currentMapDimensions)
    {
        RequirePositiveDimensions(currentMapDimensions);
        foreach (var openDimensions in openMapDimensions.Values)
        {
            RequirePositiveDimensions(openDimensions);
        }

        var errors = new List<ValidationIssue>();
        var seenSources = new HashSet<(int MapId, int MapX, int MapY)>();
        foreach (var row in rows)
        {
            var source = (row.MapId, row.MapX, row.MapY);
            if (!seenSources.Add(source))
            {
                errors.Add(new ValidationIssue(ValidationCodes.WarpDuplicateSource,
                    $"Warp source tile ({row.MapX}, {row.MapY}) on map '{row.MapId}' is defined more than once."));
            }
            if (!IsWithin(row.MapX, row.MapY, currentMapDimensions))
            {
                errors.Add(new ValidationIssue(ValidationCodes.WarpOutOfBounds,
                    $"Warp source ({row.MapX}, {row.MapY}) is outside the current map bounds " +
                    $"(0..{currentMapDimensions.Width - 1}, 0..{currentMapDimensions.Height - 1})."));
            }
            if (!maps.ContainsKey(row.WarpId))
            {
                errors.Add(new ValidationIssue(ValidationCodes.WarpDestinationMapNotFound,
                    $"Warp destination map '{row.WarpId}' was not found."));
            }
            // Negative destination coordinates block Push, so the lower bound is 0, not the SQL type minimum.
            if (row.WarpX < 0 || row.WarpX > _warpXMax || row.WarpY < 0 || row.WarpY > _warpYMax)
            {
                errors.Add(new ValidationIssue(ValidationCodes.WarpDestinationNumericRange,
                    $"Warp destination ({row.WarpX}, {row.WarpY}) is outside the allowed range " +
                    $"(0..{_warpXMax}, 0..{_warpYMax})."));
            }
            if (openMapDimensions.TryGetValue(row.WarpId, out var destination)
                && !IsWithin(row.WarpX, row.WarpY, destination))
            {
                errors.Add(new ValidationIssue(ValidationCodes.WarpDestinationOutOfBounds,
                    $"Warp destination ({row.WarpX}, {row.WarpY}) is outside map '{row.WarpId}' bounds " +
                    $"(0..{destination.Width - 1}, 0..{destination.Height - 1})."));
            }
        }

        return new ValidationResult(errors.AsReadOnly());
    }

    private static (int Min, int Max) ResolveRowRange(string columnName, string sql)
    {
        if (!IntegralSqlRanges.TryResolve(sql, out var min, out var max))
        {
            throw new InvalidDataException(
                $"Column '{columnName}' has unsupported SQL type '{sql}'; expected an integral type.");
        }
        // Row coordinates are int; a wider generated range must be rejected, not truncated.
        if (min < int.MinValue || max > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Column '{columnName}' SQL type '{sql}' has a range not representable by int row coordinates.");
        }

        return ((int)min, (int)max);
    }

    private static void RequirePositiveDimensions(MapDimensions dimensions)
    {
        if (dimensions.Width <= 0 || dimensions.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions,
                "Map dimensions must have positive width and height.");
        }
    }

    private static bool IsWithin(int x, int y, MapDimensions dimensions) =>
        x >= 0 && x < dimensions.Width && y >= 0 && y < dimensions.Height;
}

internal static class IntegralSqlRanges
{
    public static bool TryResolve(string sql, out long min, out long max)
    {
        switch (sql)
        {
            case "SMALLINT":
                min = short.MinValue;
                max = short.MaxValue;
                return true;
            case "INT":
            case "INTEGER":
                min = int.MinValue;
                max = int.MaxValue;
                return true;
            case "BIGINT":
                min = long.MinValue;
                max = long.MaxValue;
                return true;
            default:
                min = 0;
                max = 0;
                return false;
        }
    }
}
