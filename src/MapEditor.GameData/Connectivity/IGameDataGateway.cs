using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;

namespace MapEditor.GameData.Connectivity;

public interface IGameDataGateway
{
    Task<IReadOnlyList<MapReference>> ReadMapsAsync(string spreadsheetId, CancellationToken cancellationToken);

    Task<RemoteGameData> ReadGameDataAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken);

    Task<RemoteOwnedRows> ReadOwnedRowsAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken);

    Task ReplaceOwnedRowsAsync(
        string spreadsheetId,
        ReplacementPlan spawnPlan,
        ReplacementPlan warpPlan,
        CancellationToken cancellationToken);
}

public sealed record RemoteGameData
{
    public IReadOnlyList<MapReference> Maps { get; }
    public IReadOnlyDictionary<int, NpcAppearance> Npcs { get; }
    public IReadOnlyList<RemoteRow<NpcSpawnRow>> Spawns { get; }
    public IReadOnlyList<RemoteRow<WarpRow>> Warps { get; }

    public RemoteGameData(
        IReadOnlyList<MapReference> maps,
        IReadOnlyDictionary<int, NpcAppearance> npcs,
        IReadOnlyList<RemoteRow<NpcSpawnRow>> spawns,
        IReadOnlyList<RemoteRow<WarpRow>> warps)
    {
        Maps = Copy(maps, nameof(maps));
        Npcs = npcs is null
            ? throw new ArgumentNullException(nameof(npcs))
            : new Dictionary<int, NpcAppearance>(npcs);
        Spawns = Copy(spawns, nameof(spawns));
        Warps = Copy(warps, nameof(warps));
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source, string parameterName)
        => source is null
            ? throw new ArgumentNullException(parameterName)
            : source.ToList().AsReadOnly();
}

public sealed record RemoteOwnedRows
{
    public IReadOnlyList<RemoteRow<NpcSpawnRow>> Spawns { get; }
    public IReadOnlyList<RemoteRow<WarpRow>> Warps { get; }

    public RemoteOwnedRows(
        IReadOnlyList<RemoteRow<NpcSpawnRow>> spawns,
        IReadOnlyList<RemoteRow<WarpRow>> warps)
    {
        Spawns = Copy(spawns, nameof(spawns));
        Warps = Copy(warps, nameof(warps));
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source, string parameterName)
        => source is null
            ? throw new ArgumentNullException(parameterName)
            : source.ToList().AsReadOnly();
}
