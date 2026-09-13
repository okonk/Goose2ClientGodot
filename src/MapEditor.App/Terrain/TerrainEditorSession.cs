using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Terrain;

internal enum TerrainEditorChangeKind
{
    Preview,
    Committed
}

internal sealed class TerrainEditorPreparedMarkSaved
{
    public TerrainCatalog Baseline { get; }
    public int StateId { get; }
    public Action<TerrainEditorChangeKind>? Changed { get; }

    internal TerrainEditorPreparedMarkSaved(TerrainCatalog baseline, int stateId, Action<TerrainEditorChangeKind>? changed)
    {
        Baseline = baseline;
        StateId = stateId;
        Changed = changed;
    }
}

internal sealed class TerrainEditorSession
{
    private static readonly TerrainPeer[] PeerSlots =
    {
        TerrainPeer.North, TerrainPeer.East, TerrainPeer.South, TerrainPeer.West,
        TerrainPeer.NorthEast, TerrainPeer.SouthEast, TerrainPeer.SouthWest, TerrainPeer.NorthWest
    };

    private readonly SpriteManifest _manifest;
    private readonly List<TerrainDefinition> _terrains;
    private readonly TerrainDraftGraphics _graphics;
    private readonly LinkedList<TerrainEditorCommand> _undo = new();
    private readonly LinkedList<TerrainEditorCommand> _redo = new();

    private int _currentStateId;
    private int _pushedStateId;
    private int _nextStateId = 1;

    private TerrainCatalog _baseline;
    private TerrainCatalog _currentCatalog = null!;
    private IReadOnlyList<TerrainValidationIssue> _diagnostics = null!;
    private bool _hasErrors;

    private bool _strokeActive;
    private Guid? _strokeValue;
    private Dictionary<TerrainGraphicReference, (TerrainPattern? Pattern, int Index)>? _strokeBefore;

    public bool IsDirty => _currentStateId != _pushedStateId;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool CanSave => !_hasErrors;

    public TerrainCatalog CurrentCatalog => _currentCatalog;

    public IReadOnlyList<TerrainValidationIssue> Diagnostics => _diagnostics;

    public event Action<TerrainEditorChangeKind>? Changed;

    internal TerrainEditorSession(TerrainCatalog baseline, SpriteManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(manifest);
        _manifest = manifest;
        _baseline = baseline;
        _terrains = new List<TerrainDefinition>(baseline.Terrains);
        _graphics = new TerrainDraftGraphics();
        foreach (var graphic in baseline.Graphics)
        {
            _graphics.AddInitial(graphic.Reference, graphic.Pattern);
        }

        Rebuild();
    }

    public Guid AddTerrain()
    {
        GuardNoActiveStroke();
        var definition = new TerrainDefinition(Guid.NewGuid(), NextDefaultName(), null);
        var beforeStateId = _currentStateId;
        var afterStateId = AdvanceStateId();
        _terrains.Add(definition);
        Push(new TerrainAddCommand(definition, beforeStateId, afterStateId));
        Rebuild();
        Raise(TerrainEditorChangeKind.Committed);
        return definition.Id;
    }

    public bool RenameTerrain(Guid id, string name)
    {
        GuardNoActiveStroke();
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        var index = _terrains.FindIndex(terrain => terrain.Id == id);
        if (index < 0)
        {
            return false;
        }

        var before = _terrains[index];
        if (before.Name == name)
        {
            return true;
        }

        var after = before with { Name = name };
        var beforeStateId = _currentStateId;
        var afterStateId = AdvanceStateId();
        _terrains[index] = after;
        Push(new TerrainUpdateCommand(before, after, beforeStateId, afterStateId));
        Rebuild();
        Raise(TerrainEditorChangeKind.Committed);
        return true;
    }

    public bool SetColorOverride(Guid id, TerrainColor? color)
    {
        GuardNoActiveStroke();
        var index = _terrains.FindIndex(terrain => terrain.Id == id);
        if (index < 0)
        {
            return false;
        }

        var before = _terrains[index];
        if (before.ColorOverride == color)
        {
            return true;
        }

        var after = before with { ColorOverride = color };
        var beforeStateId = _currentStateId;
        var afterStateId = AdvanceStateId();
        _terrains[index] = after;
        Push(new TerrainUpdateCommand(before, after, beforeStateId, afterStateId));
        Rebuild();
        Raise(TerrainEditorChangeKind.Committed);
        return true;
    }

    public bool DeleteTerrain(Guid id)
    {
        GuardNoActiveStroke();
        var terrainIndex = _terrains.FindIndex(terrain => terrain.Id == id);
        if (terrainIndex < 0)
        {
            return false;
        }

        var definition = _terrains[terrainIndex];
        var removed = new List<(TerrainGraphicReference Reference, TerrainPattern Pattern, int Index)>();
        var removedByClear = new List<(TerrainGraphicReference Reference, TerrainPattern Pattern, int Index)>();
        var cleared = new List<(TerrainGraphicReference Reference, TerrainPeer Peer)>();

        var order = _graphics.Order;
        for (var i = 0; i < order.Count; i++)
        {
            var reference = order[i];
            _graphics.TryGet(reference, out var pattern);
            if (pattern.Center == id)
            {
                removed.Add((reference, pattern, i));
                continue;
            }

            var entryCleared = new List<TerrainPeer>();
            var clearedPattern = pattern;
            foreach (var peer in PeerSlots)
            {
                if (clearedPattern.Get(peer) == id)
                {
                    clearedPattern = TerrainDraftGraphics.SetPeer(clearedPattern, peer, null);
                    entryCleared.Add(peer);
                }
            }

            if (entryCleared.Count == 0)
            {
                continue;
            }

            if (TerrainDraftGraphics.IsAllNone(clearedPattern))
            {
                removedByClear.Add((reference, pattern, i));
            }
            else
            {
                foreach (var peer in entryCleared)
                {
                    cleared.Add((reference, peer));
                }
            }
        }

        var beforeStateId = _currentStateId;
        var afterStateId = AdvanceStateId();
        _terrains.RemoveAt(terrainIndex);
        foreach (var (reference, _, _) in removed)
        {
            _graphics.Remove(reference);
        }

        foreach (var (reference, _, _) in removedByClear)
        {
            _graphics.Remove(reference);
        }

        foreach (var (reference, peer) in cleared)
        {
            _graphics.SetSlot(reference, peer, null);
        }

        Push(new TerrainDeleteCommand(id, definition, terrainIndex, removed, removedByClear, cleared, beforeStateId, afterStateId));
        Rebuild();
        Raise(TerrainEditorChangeKind.Committed);
        return true;
    }

    public void BeginRegionStroke(Guid? value)
    {
        GuardNoActiveStroke();
        _strokeActive = true;
        _strokeValue = value;
        _strokeBefore = new Dictionary<TerrainGraphicReference, (TerrainPattern?, int)>();
    }

    public void VisitRegion(TerrainRegionKey key)
    {
        if (!_strokeActive)
        {
            throw new InvalidOperationException("No region stroke is active.");
        }

        var existed = _graphics.TryGet(key.Graphic, out var pattern);
        if (pattern.Get(key.Peer) == _strokeValue)
        {
            return;
        }

        if (!_strokeBefore.ContainsKey(key.Graphic))
        {
            _strokeBefore[key.Graphic] = existed
                ? (pattern, _graphics.IndexOf(key.Graphic))
                : (null, -1);
        }

        _graphics.SetSlot(key.Graphic, key.Peer, _strokeValue);
        Raise(TerrainEditorChangeKind.Preview);
    }

    public bool CompleteRegionStroke()
    {
        if (!_strokeActive)
        {
            return false;
        }

        var before = _strokeBefore!;
        _strokeActive = false;
        _strokeValue = null;
        _strokeBefore = null;
        if (before.Count == 0)
        {
            return true;
        }

        var affected = new List<(TerrainGraphicReference Reference, TerrainPattern? Before, int BeforeIndex, TerrainPattern? After, int AfterIndex)>(before.Count);
        foreach (var (reference, (beforePattern, beforeIndex)) in before)
        {
            TerrainPattern? afterPattern;
            int afterIndex;
            if (_graphics.TryGet(reference, out var pattern))
            {
                afterPattern = pattern;
                afterIndex = _graphics.IndexOf(reference);
            }
            else
            {
                afterPattern = null;
                afterIndex = -1;
            }
            affected.Add((reference, beforePattern, beforeIndex, afterPattern, afterIndex));
        }

        var beforeStateId = _currentStateId;
        var afterStateId = AdvanceStateId();
        Push(new TerrainRegionCommand(affected, beforeStateId, afterStateId));
        Rebuild();
        Raise(TerrainEditorChangeKind.Committed);
        return true;
    }

    public void CancelRegionStroke()
    {
        if (!_strokeActive)
        {
            return;
        }

        var before = _strokeBefore!;
        _strokeActive = false;
        _strokeValue = null;
        _strokeBefore = null;
        if (before.Count == 0)
        {
            return;
        }

        _graphics.Restore(
            before.Keys,
            before
                .Where(entry => entry.Value.Pattern is not null)
                .Select(entry => (entry.Key, entry.Value.Pattern!.Value, entry.Value.Index)));
        Raise(TerrainEditorChangeKind.Preview);
    }

    public bool Undo()
    {
        GuardNoActiveStroke();
        if (_undo.Count == 0)
        {
            return false;
        }

        var command = _undo.Last!.Value;
        _undo.RemoveLast();
        command.Replay(_terrains, _graphics, reverse: true);
        _currentStateId = command.BeforeStateId;
        _redo.AddFirst(command);
        Rebuild();
        Raise(TerrainEditorChangeKind.Committed);
        return true;
    }

    public bool Redo()
    {
        GuardNoActiveStroke();
        if (_redo.Count == 0)
        {
            return false;
        }

        var command = _redo.First!.Value;
        _redo.RemoveFirst();
        command.Replay(_terrains, _graphics, reverse: false);
        _currentStateId = command.AfterStateId;
        _undo.AddLast(command);
        Rebuild();
        Raise(TerrainEditorChangeKind.Committed);
        return true;
    }

    public void MarkSaved(TerrainCatalog baseline)
    {
        var prepared = PrepareMarkSaved(baseline);
        ApplyMarkSaved(prepared);
        NotifyMarkSaved(prepared);
    }

    // The prepare captures everything apply/notify need so a failed file save between the
    // stages leaves the session untouched.
    public TerrainEditorPreparedMarkSaved PrepareMarkSaved(TerrainCatalog baseline)
    {
        GuardNoActiveStroke();
        ArgumentNullException.ThrowIfNull(baseline);
        return new TerrainEditorPreparedMarkSaved(baseline, _currentStateId, Changed);
    }

    public void ApplyMarkSaved(TerrainEditorPreparedMarkSaved prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        _pushedStateId = prepared.StateId;
        _baseline = prepared.Baseline;
    }

    public void NotifyMarkSaved(TerrainEditorPreparedMarkSaved prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        prepared.Changed?.Invoke(TerrainEditorChangeKind.Committed);
    }

    public void Revert()
    {
        GuardNoActiveStroke();
        ResetDraft(_baseline);
        _undo.Clear();
        _redo.Clear();
        _currentStateId = _pushedStateId;
        Rebuild();
        Raise(TerrainEditorChangeKind.Committed);
    }

    private int AdvanceStateId()
    {
        var afterStateId = _nextStateId++;
        _currentStateId = afterStateId;
        return afterStateId;
    }

    private void Push(TerrainEditorCommand command)
    {
        _redo.Clear();
        _undo.AddLast(command);
    }

    private void GuardNoActiveStroke()
    {
        if (_strokeActive)
        {
            throw new InvalidOperationException("A region stroke is active; cancel or complete it first.");
        }
    }

    private string NextDefaultName()
    {
        var taken = new HashSet<string>(_terrains.Select(terrain => terrain.Name), StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? "Terrain" : $"Terrain {n}";
            if (taken.Add(name))
            {
                return name;
            }
        }
    }

    private void ResetDraft(TerrainCatalog catalog)
    {
        _terrains.Clear();
        _terrains.AddRange(catalog.Terrains);
        _graphics.Clear();
        foreach (var graphic in catalog.Graphics)
        {
            _graphics.AddInitial(graphic.Reference, graphic.Pattern);
        }
    }

    private void Rebuild()
    {
        var graphics = new List<TerrainGraphicDefinition>(_graphics.Count);
        foreach (var reference in _graphics.Order)
        {
            _graphics.TryGet(reference, out var pattern);
            graphics.Add(new TerrainGraphicDefinition(reference, pattern));
        }

        _currentCatalog = new TerrainCatalog(_terrains, graphics);
        var validation = TerrainAssetCatalog.Validate(_currentCatalog, _manifest);
        _diagnostics = validation.Issues;
        _hasErrors = !validation.IsValid;
    }

    private void Raise(TerrainEditorChangeKind kind)
        => Changed?.Invoke(kind);
}
