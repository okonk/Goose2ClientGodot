using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Editing;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Snapshots;

namespace MapEditor.GameData.Sync;

public sealed class GameDataSyncSession
{
    private SheetEditSession _edits = null!;
    private SpawnSnapshot _pulledSpawns = null!;
    private WarpSnapshot _pulledWarps = null!;
    private IReadOnlyDictionary<int, NpcAppearance> _npcs = null!;
    private IReadOnlyList<MapReference> _maps = null!;
    private bool _requiresPull;

    public GameDataSyncSession(string spreadsheetId, int mapId, RemoteGameData pulled)
    {
        SpreadsheetId = spreadsheetId ?? throw new ArgumentNullException(nameof(spreadsheetId));
        MapId = mapId;
        if (pulled is null)
        {
            throw new ArgumentNullException(nameof(pulled));
        }

        ApplyPulled(pulled);
    }

    public string SpreadsheetId { get; }

    public int MapId { get; }

    public SheetEditSession Edits => _edits;

    public SpawnSnapshot PulledSpawns => _pulledSpawns;

    public WarpSnapshot PulledWarps => _pulledWarps;

    public IReadOnlyDictionary<int, NpcAppearance> Npcs => _npcs;

    public IReadOnlyList<MapReference> Maps => _maps;

    public bool RequiresPull => _requiresPull;

    // Only the coordinator may publish remote state, mark pushed, or clear RequiresPull.
    internal void ApplyPulled(RemoteGameData pulled)
    {
        if (pulled is null)
        {
            throw new ArgumentNullException(nameof(pulled));
        }

        var spawns = Filter(pulled.Spawns, MapId);
        var warps = Filter(pulled.Warps, MapId);
        _pulledSpawns = new SpawnSnapshot(spawns);
        _pulledWarps = new WarpSnapshot(warps);
        _edits = new SheetEditSession(spawns, warps);
        _npcs = pulled.Npcs;
        _maps = pulled.Maps;
        _requiresPull = false;
    }

    internal void MarkPushed()
    {
        _edits.MarkPushed();
        _pulledSpawns = new SpawnSnapshot(_edits.Spawns);
        _pulledWarps = new WarpSnapshot(_edits.Warps);
    }

    internal void MarkAmbiguous()
    {
        _requiresPull = true;
    }

    private static List<NpcSpawnRow> Filter(IReadOnlyList<RemoteRow<NpcSpawnRow>> rows, int mapId)
        => rows.Where(row => row.Value.MapId == mapId).Select(row => row.Value).ToList();

    private static List<WarpRow> Filter(IReadOnlyList<RemoteRow<WarpRow>> rows, int mapId)
        => rows.Where(row => row.Value.MapId == mapId).Select(row => row.Value).ToList();
}
