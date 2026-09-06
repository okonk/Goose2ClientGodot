using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using MapEditor.GameData.Schema;

namespace MapEditor.GameData.Rows;

public sealed class SheetRowMapper
{
    private sealed record Field(SheetSchema Sheet, ColumnSchema Column, int Index);

    private readonly Field _mapId;
    private readonly Field _mapName;
    private readonly Field _mapFilename;

    private readonly Field _npcId;
    private readonly Field _npcName;
    private readonly Field _bodyState;
    private readonly Field _bodyId;
    private readonly Field _bodyR;
    private readonly Field _bodyG;
    private readonly Field _bodyB;
    private readonly Field _bodyA;
    private readonly Field _faceId;
    private readonly Field _hairId;
    private readonly Field _hairR;
    private readonly Field _hairG;
    private readonly Field _hairB;
    private readonly Field _hairA;
    private readonly Field _equippedItems;

    private readonly Field _spawnNpcId;
    private readonly Field _spawnMapId;
    private readonly Field _spawnMapX;
    private readonly Field _spawnMapY;

    private readonly Field _warpMapId;
    private readonly Field _warpMapX;
    private readonly Field _warpMapY;
    private readonly Field _warpId;
    private readonly Field _warpX;
    private readonly Field _warpY;

    public SheetRowMapper(GameDataSchema schema)
    {
        var maps = schema.GetRequiredSheet("Maps");
        var npcs = schema.GetRequiredSheet("NPCs");
        var spawns = schema.GetRequiredSheet("NPC Spawns");
        var warptiles = schema.GetRequiredSheet("Warptiles");

        _mapId = Resolve(maps, "map_id");
        _mapName = Resolve(maps, "map_name");
        _mapFilename = Resolve(maps, "map_filename");

        _npcId = Resolve(npcs, "npc_id");
        _npcName = Resolve(npcs, "npc_name");
        _bodyState = Resolve(npcs, "body_state");
        _bodyId = Resolve(npcs, "body_id");
        _bodyR = Resolve(npcs, "body_r");
        _bodyG = Resolve(npcs, "body_g");
        _bodyB = Resolve(npcs, "body_b");
        _bodyA = Resolve(npcs, "body_a");
        _faceId = Resolve(npcs, "face_id");
        _hairId = Resolve(npcs, "hair_id");
        _hairR = Resolve(npcs, "hair_r");
        _hairG = Resolve(npcs, "hair_g");
        _hairB = Resolve(npcs, "hair_b");
        _hairA = Resolve(npcs, "hair_a");
        _equippedItems = Resolve(npcs, "equipped_items");

        _spawnNpcId = Resolve(spawns, "npc_id");
        _spawnMapId = Resolve(spawns, "map_id");
        _spawnMapX = Resolve(spawns, "map_x");
        _spawnMapY = Resolve(spawns, "map_y");

        _warpMapId = Resolve(warptiles, "map_id");
        _warpMapX = Resolve(warptiles, "map_x");
        _warpMapY = Resolve(warptiles, "map_y");
        _warpId = Resolve(warptiles, "warp_id");
        _warpX = Resolve(warptiles, "warp_x");
        _warpY = Resolve(warptiles, "warp_y");
    }

    public MapReference MapMap(IReadOnlyList<string?> cells)
    {
        return new MapReference(
            ReadInt(_mapId, cells),
            ReadText(_mapName, cells),
            ReadText(_mapFilename, cells));
    }

    public NpcAppearance MapNpc(IReadOnlyList<string?> cells)
    {
        return new NpcAppearance(
            ReadInt(_npcId, cells),
            ReadText(_npcName, cells),
            ReadInt(_bodyState, cells),
            ReadInt(_bodyId, cells),
            new RgbaValue(
                ReadInt(_bodyR, cells),
                ReadInt(_bodyG, cells),
                ReadInt(_bodyB, cells),
                ReadInt(_bodyA, cells)),
            ReadInt(_faceId, cells),
            ReadInt(_hairId, cells),
            new RgbaValue(
                ReadInt(_hairR, cells),
                ReadInt(_hairG, cells),
                ReadInt(_hairB, cells),
                ReadInt(_hairA, cells)),
            ReadText(_equippedItems, cells));
    }

    public NpcSpawnRow MapSpawn(IReadOnlyList<string?> cells)
    {
        return new NpcSpawnRow(
            ReadInt(_spawnNpcId, cells),
            ReadInt(_spawnMapId, cells),
            ReadInt(_spawnMapX, cells),
            ReadInt(_spawnMapY, cells));
    }

    public WarpRow MapWarp(IReadOnlyList<string?> cells)
    {
        return new WarpRow(
            ReadInt(_warpMapId, cells),
            ReadInt(_warpMapX, cells),
            ReadInt(_warpMapY, cells),
            ReadInt(_warpId, cells),
            ReadInt(_warpX, cells),
            ReadInt(_warpY, cells));
    }

    public IReadOnlyList<string?> ToCells(NpcSpawnRow row)
    {
        var cells = new string?[_spawnNpcId.Sheet.Columns.Count];
        cells[_spawnNpcId.Index] = Format(row.NpcId);
        cells[_spawnMapId.Index] = Format(row.MapId);
        cells[_spawnMapX.Index] = Format(row.MapX);
        cells[_spawnMapY.Index] = Format(row.MapY);
        return new ReadOnlyCollection<string?>(cells);
    }

    public IReadOnlyList<string?> ToCells(WarpRow row)
    {
        var cells = new string?[_warpMapId.Sheet.Columns.Count];
        cells[_warpMapId.Index] = Format(row.MapId);
        cells[_warpMapX.Index] = Format(row.MapX);
        cells[_warpMapY.Index] = Format(row.MapY);
        cells[_warpId.Index] = Format(row.WarpId);
        cells[_warpX.Index] = Format(row.WarpX);
        cells[_warpY.Index] = Format(row.WarpY);
        return new ReadOnlyCollection<string?>(cells);
    }

    private static Field Resolve(SheetSchema sheet, string name)
    {
        var column = sheet.GetRequiredColumn(name);
        return new Field(sheet, column, sheet.GetColumnIndex(name));
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? Cell(IReadOnlyList<string?> cells, int index) =>
        index < cells.Count ? cells[index] : null;

    private static int ReadInt(Field field, IReadOnlyList<string?> cells)
    {
        var raw = Cell(cells, field.Index);
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (field.Column.Required)
            {
                throw new FormatException(
                    $"Sheet '{field.Sheet.Sheet}' column '{field.Column.Name}' is required but has a blank value.");
            }
            return ParseIntDefault(field, field.Column.Default);
        }
        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        throw new FormatException(
            $"Sheet '{field.Sheet.Sheet}' column '{field.Column.Name}' has a malformed integer value: '{raw}'.");
    }

    private static string ReadText(Field field, IReadOnlyList<string?> cells)
    {
        var raw = Cell(cells, field.Index);
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (field.Column.Required)
            {
                throw new FormatException(
                    $"Sheet '{field.Sheet.Sheet}' column '{field.Column.Name}' is required but has a blank value.");
            }
            return ParseTextDefault(field, field.Column.Default);
        }
        return raw.Trim();
    }

    private static int ParseIntDefault(Field field, string? def)
    {
        if (def is null)
        {
            throw new FormatException(
                $"Sheet '{field.Sheet.Sheet}' column '{field.Column.Name}' is optional but has no default.");
        }
        if (int.TryParse(def.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        throw new FormatException(
            $"Sheet '{field.Sheet.Sheet}' column '{field.Column.Name}' has an unsupported integer default: '{def}'.");
    }

    private static string ParseTextDefault(Field field, string? def)
    {
        if (def is null)
        {
            throw new FormatException(
                $"Sheet '{field.Sheet.Sheet}' column '{field.Column.Name}' is optional but has no default.");
        }
        var trimmed = def.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            var quoteCount = 0;
            foreach (var ch in trimmed)
            {
                if (ch == '\'')
                {
                    quoteCount++;
                }
            }
            if (quoteCount % 2 == 0)
            {
                return trimmed[1..^1].Replace("''", "'");
            }
        }
        throw new FormatException(
            $"Sheet '{field.Sheet.Sheet}' column '{field.Column.Name}' has an unsupported text default: '{def}'.");
    }
}
