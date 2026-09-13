using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;

namespace MapEditor.App.Terrain;

internal abstract class TerrainEditorCommand
{
    public int BeforeStateId { get; }
    public int AfterStateId { get; }

    protected TerrainEditorCommand(int beforeStateId, int afterStateId)
    {
        BeforeStateId = beforeStateId;
        AfterStateId = afterStateId;
    }

    public abstract void Replay(List<TerrainDefinition> terrains, TerrainDraftGraphics graphics, bool reverse);
}

internal sealed class TerrainAddCommand : TerrainEditorCommand
{
    private readonly TerrainDefinition _definition;

    public TerrainAddCommand(TerrainDefinition definition, int beforeStateId, int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        _definition = definition;
    }

    public override void Replay(List<TerrainDefinition> terrains, TerrainDraftGraphics graphics, bool reverse)
    {
        if (reverse)
        {
            terrains.Remove(_definition);
        }
        else
        {
            terrains.Add(_definition);
        }
    }
}

internal sealed class TerrainUpdateCommand : TerrainEditorCommand
{
    private readonly TerrainDefinition _before;
    private readonly TerrainDefinition _after;

    public TerrainUpdateCommand(TerrainDefinition before, TerrainDefinition after, int beforeStateId, int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        _before = before;
        _after = after;
    }

    public override void Replay(List<TerrainDefinition> terrains, TerrainDraftGraphics graphics, bool reverse)
    {
        var index = terrains.FindIndex(terrain => terrain.Id == _before.Id);
        terrains[index] = reverse ? _before : _after;
    }
}

internal sealed class TerrainDeleteCommand : TerrainEditorCommand
{
    private readonly Guid _id;
    private readonly TerrainDefinition _definition;
    private readonly int _terrainIndex;
    private readonly List<(TerrainGraphicReference Reference, TerrainPattern Pattern, int Index)> _removed;
    private readonly List<(TerrainGraphicReference Reference, TerrainPattern Pattern, int Index)> _removedByClear;
    private readonly List<(TerrainGraphicReference Reference, TerrainPeer Peer)> _cleared;

    public TerrainDeleteCommand(
        Guid id,
        TerrainDefinition definition,
        int terrainIndex,
        List<(TerrainGraphicReference Reference, TerrainPattern Pattern, int Index)> removed,
        List<(TerrainGraphicReference Reference, TerrainPattern Pattern, int Index)> removedByClear,
        List<(TerrainGraphicReference Reference, TerrainPeer Peer)> cleared,
        int beforeStateId,
        int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        _id = id;
        _definition = definition;
        _terrainIndex = terrainIndex;
        _removed = removed;
        _removedByClear = removedByClear;
        _cleared = cleared;
    }

    public override void Replay(List<TerrainDefinition> terrains, TerrainDraftGraphics graphics, bool reverse)
    {
        if (reverse)
        {
            terrains.Insert(_terrainIndex, _definition);
            var references = _removed.Select(entry => entry.Reference).Concat(_removedByClear.Select(entry => entry.Reference)).ToList();
            graphics.Restore(references, _removed.Concat(_removedByClear));
            foreach (var (reference, peer) in _cleared)
            {
                graphics.SetSlot(reference, peer, _id);
            }
        }
        else
        {
            terrains.RemoveAt(_terrainIndex);
            foreach (var (reference, _, _) in _removed)
            {
                graphics.Remove(reference);
            }

            foreach (var (reference, _, _) in _removedByClear)
            {
                graphics.Remove(reference);
            }

            foreach (var (reference, peer) in _cleared)
            {
                graphics.SetSlot(reference, peer, null);
            }
        }
    }
}

internal sealed class TerrainRegionCommand : TerrainEditorCommand
{
    private readonly List<(TerrainGraphicReference Reference, TerrainPattern? Before, int BeforeIndex, TerrainPattern? After, int AfterIndex)> _affected;

    public TerrainRegionCommand(
        List<(TerrainGraphicReference Reference, TerrainPattern? Before, int BeforeIndex, TerrainPattern? After, int AfterIndex)> affected,
        int beforeStateId,
        int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        _affected = affected;
    }

    public override void Replay(List<TerrainDefinition> terrains, TerrainDraftGraphics graphics, bool reverse)
    {
        var references = _affected.Select(entry => entry.Reference).ToList();
        graphics.Restore(references, reverse
            ? _affected.Where(entry => entry.Before is not null)
                .Select(entry => (entry.Reference, entry.Before!.Value, entry.BeforeIndex))
            : _affected.Where(entry => entry.After is not null)
                .Select(entry => (entry.Reference, entry.After!.Value, entry.AfterIndex)));
    }
}

internal sealed class TerrainDraftGraphics
{
    private static readonly TerrainPeer[] AllPeers =
    {
        TerrainPeer.Center, TerrainPeer.North, TerrainPeer.East, TerrainPeer.South, TerrainPeer.West,
        TerrainPeer.NorthEast, TerrainPeer.SouthEast, TerrainPeer.SouthWest, TerrainPeer.NorthWest
    };

    private readonly List<TerrainGraphicReference> _order = new();
    private readonly Dictionary<TerrainGraphicReference, TerrainPattern> _patterns = new();

    public int Count => _order.Count;

    public IReadOnlyList<TerrainGraphicReference> Order => _order;

    public bool TryGet(TerrainGraphicReference reference, out TerrainPattern pattern)
        => _patterns.TryGetValue(reference, out pattern!);

    public int IndexOf(TerrainGraphicReference reference)
        => _order.IndexOf(reference);

    public void AddInitial(TerrainGraphicReference reference, TerrainPattern pattern)
    {
        _order.Add(reference);
        _patterns[reference] = pattern;
    }

    public void SetSlot(TerrainGraphicReference reference, TerrainPeer peer, Guid? value)
    {
        var existed = _patterns.TryGetValue(reference, out var pattern);
        if (!existed)
        {
            if (value is null)
            {
                return;
            }

            pattern = default;
            _order.Add(reference);
        }

        pattern = SetPeer(pattern, peer, value);
        if (IsAllNone(pattern))
        {
            _order.Remove(reference);
            _patterns.Remove(reference);
            return;
        }

        _patterns[reference] = pattern;
    }

    public bool Remove(TerrainGraphicReference reference)
    {
        if (!_patterns.Remove(reference))
        {
            return false;
        }

        _order.Remove(reference);
        return true;
    }

    public void Clear()
    {
        _order.Clear();
        _patterns.Clear();
    }

    // Recorded indices are positions in the source state; inserting them in ascending order
    // rebuilds the exact source order because unaffected entries keep their relative order.
    public void Restore(
        IEnumerable<TerrainGraphicReference> affected,
        IEnumerable<(TerrainGraphicReference Reference, TerrainPattern Pattern, int Index)> entries)
    {
        var list = entries.ToList();
        foreach (var reference in affected)
        {
            Remove(reference);
        }

        foreach (var (reference, pattern, index) in list.OrderBy(entry => entry.Index))
        {
            _order.Insert(index, reference);
            _patterns[reference] = pattern;
        }
    }

    public static TerrainPattern SetPeer(TerrainPattern pattern, TerrainPeer peer, Guid? value)
        => peer switch
        {
            TerrainPeer.Center => pattern with { Center = value },
            TerrainPeer.North => pattern with { North = value },
            TerrainPeer.East => pattern with { East = value },
            TerrainPeer.South => pattern with { South = value },
            TerrainPeer.West => pattern with { West = value },
            TerrainPeer.NorthEast => pattern with { NorthEast = value },
            TerrainPeer.SouthEast => pattern with { SouthEast = value },
            TerrainPeer.SouthWest => pattern with { SouthWest = value },
            _ => pattern with { NorthWest = value }
        };

    public static bool IsAllNone(TerrainPattern pattern)
    {
        foreach (var peer in AllPeers)
        {
            if (pattern.Get(peer) is not null)
            {
                return false;
            }
        }

        return true;
    }
}
