using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.GameData.Rows;

namespace MapEditor.GameData.Replacement;

public sealed class ReplacementPlanner
{
    private readonly SheetRowMapper _mapper;

    public ReplacementPlanner(SheetRowMapper mapper)
    {
        _mapper = mapper;
    }

    public ReplacementPlan PlanSpawnReplacement(
        IReadOnlyList<RemoteRow<NpcSpawnRow>> currentRemote,
        IReadOnlyList<NpcSpawnRow> desiredLocal,
        int mapId)
    {
        return Plan("NPC Spawns", currentRemote, desiredLocal, mapId,
            row => row.MapId, _mapper.ToCells);
    }

    public ReplacementPlan PlanWarpReplacement(
        IReadOnlyList<RemoteRow<WarpRow>> currentRemote,
        IReadOnlyList<WarpRow> desiredLocal,
        int mapId)
    {
        return Plan("Warptiles", currentRemote, desiredLocal, mapId,
            row => row.MapId, _mapper.ToCells);
    }

    private static ReplacementPlan Plan<T>(
        string sheet,
        IReadOnlyList<RemoteRow<T>> currentRemote,
        IReadOnlyList<T> desiredLocal,
        int mapId,
        Func<T, int> mapIdOf,
        Func<T, IReadOnlyList<string?>> toCells)
        where T : struct
    {
        var targetRows = new List<RemoteRow<T>>();
        foreach (var remote in currentRemote)
        {
            if (remote.WorksheetRowNumber <= 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentRemote),
                    "WorksheetRowNumber is one-based including the header; data rows start at 2.");
            }
            if (mapIdOf(remote.Value) == mapId)
            {
                targetRows.Add(remote);
            }
        }

        var unmatchedRemote = new Dictionary<T, Queue<int>>();
        foreach (var remote in targetRows)
        {
            if (!unmatchedRemote.TryGetValue(remote.Value, out var queue))
            {
                queue = new Queue<int>();
                unmatchedRemote[remote.Value] = queue;
            }
            queue.Enqueue(remote.WorksheetRowNumber);
        }

        var inserts = new List<RowInsert>();
        foreach (var desired in desiredLocal)
        {
            if (mapIdOf(desired) != mapId)
            {
                continue;
            }
            if (unmatchedRemote.TryGetValue(desired, out var queue) && queue.Count > 0)
            {
                queue.Dequeue();
            }
            else
            {
                inserts.Add(new RowInsert(toCells(desired)));
            }
        }

        // Descending so a gateway can apply deletes top-down without index shifting.
        var deletes = unmatchedRemote
            .SelectMany(entry => entry.Value)
            .OrderByDescending(rowNumber => rowNumber)
            .Select(rowNumber => new RowDelete(rowNumber))
            .ToList();

        return new ReplacementPlan(sheet, deletes.AsReadOnly(), inserts.AsReadOnly());
    }
}
