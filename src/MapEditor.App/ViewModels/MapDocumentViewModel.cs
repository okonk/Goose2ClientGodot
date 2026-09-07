using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.Core;
using MapEditor.GameData.Editing;
using MapEditor.GameData.Rows;

namespace MapEditor.App.ViewModels;

[Flags]
internal enum EditorRefresh
{
    None = 0,
    Title = 1 << 0,
    Commands = 1 << 1,
    Canvas = 1 << 2,
    Palette = 1 << 3
}

internal sealed class MapDocumentViewModel : ViewModelBase, IDisposable
{
    private const string UntitledName = "Untitled";

    private readonly EditorDocumentController _controller;
    private readonly SharedTileClipboard _clipboard;
    private MapEditSession _session;
    private SheetEditSession _sheetSession;
    private DocumentEditTimeline _timeline;
    private DocumentGameDataState? _gameData;

    private MapEditTool _activeTool = MapEditTool.Pencil;
    private IReadOnlyList<int> _sheetIds = Array.Empty<int>();
    private int _selectedSheet;
    private byte _layerVisibility = 0b11111;
    private bool _showGrid = true;
    private bool _showBlocked;
    private int? _hoverX;
    private int? _hoverY;
    private int? _selectedX;
    private int? _selectedY;
    private bool _pasteMode;
    private MapTileRectangle? _selectionRectangle;
    private int _zoomPercent = 100;
    private int _mapWidth;
    private int _mapHeight;
    private string _title;
    private string _tabTitle;
    private string _tabToolTip;
    private bool _lastNotifiedDirty;
    private bool _canUndo;
    private bool _canRedo;
    private bool _canSave;

    public MapDocumentViewModel(EditorDocumentController controller, SharedTileClipboard clipboard)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _session = _controller.Document.Session;
        _sheetSession = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        _timeline = new DocumentEditTimeline(_session, _sheetSession);
        _mapWidth = _session.Document.Width;
        _mapHeight = _session.Document.Height;
        _title = BuildTitle();
        _tabTitle = BuildTabTitle();
        _tabToolTip = _controller.Document.Path ?? UntitledName;
        _lastNotifiedDirty = _session.IsDirty;
        _canSave = _session.IsDirty;
        _controller.StateChanged += OnControllerStateChanged;
        _session.Resized += OnSessionResized;
        _clipboard.Changed += OnClipboardChanged;
    }

    public void Dispose()
    {
        // the shared clipboard outlives every document; unsubscribing here lets a closed tab be collected
        _clipboard.Changed -= OnClipboardChanged;
        _controller.StateChanged -= OnControllerStateChanged;
        _session.Resized -= OnSessionResized;
        _timeline.Detach();
        if (_gameData is not null)
        {
            _gameData.Changed -= OnGameDataChanged;
            _gameData.Dispose();
            _gameData = null;
        }
    }

    public event Action? CanvasInvalidated;

    public event Action? PaletteInvalidated;

    public event Action<ErrorPresentation>? GameDataError;

    public MapEditSession Session => _controller.Document.Session;

    internal SheetEditSession SheetSession => _sheetSession;

    internal DocumentEditTimeline Timeline => _timeline;

    internal DocumentGameDataState? GameData => _gameData;

    internal void AttachGameData(DocumentGameDataState state)
    {
        _gameData = state ?? throw new ArgumentNullException(nameof(state));
        _gameData.Changed += OnGameDataChanged;
    }

    internal void AttachSheetSession(SheetEditSession session)
    {
        _sheetSession = session ?? throw new ArgumentNullException(nameof(session));
        _timeline = _timeline.CreateForNewSheetSession(_sheetSession);
    }

    internal EditorDocument Document => _controller.Document;

    public MapEditTool ActiveTool
    {
        get => _activeTool;
        set
        {
            if (value < MapEditTool.Pencil || value > MapEditTool.FloodFill)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_gameData is not null)
            {
                _gameData.ActiveTool = GameDataTool.None;
            }

            if (value != MapEditTool.MultiSelect && _selectionRectangle is not null)
            {
                SelectionRectangle = null;
                Refresh(EditorRefresh.Canvas);
            }

            SetField(ref _activeTool, value);
        }
    }

    public byte SelectedLayers
    {
        get => _session.SelectedLayers;
        set
        {
            if (_session.SelectedLayers != value)
            {
                _session.SelectedLayers = value;
                OnPropertyChanged();
                CancelPasteMode();
            }
        }
    }

    public int TopLayer => _session.TopLayer;

    internal int LayerAnchor { get; set; }

    public MapTileLayer Brush
    {
        get => _session.SelectedTileLayer;
        set
        {
            if (_session.SelectedTileLayer != value)
            {
                _session.SelectedTileLayer = value;
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<int> SheetIds => _sheetIds;

    internal void SetSheetIds(IReadOnlyList<int> sheetIds)
    {
        _sheetIds = sheetIds ?? throw new ArgumentNullException(nameof(sheetIds));
        OnPropertyChanged(nameof(SheetIds));
    }

    public int SelectedSheet
    {
        get => _selectedSheet;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetField(ref _selectedSheet, value);
        }
    }

    public byte LayerVisibility
    {
        get => _layerVisibility;
        set
        {
            if (value > 0b11111)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (SetField(ref _layerVisibility, value))
            {
                Refresh(EditorRefresh.Canvas);
            }
        }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (SetField(ref _showGrid, value))
            {
                Refresh(EditorRefresh.Canvas);
            }
        }
    }

    public bool ShowBlocked
    {
        get => _showBlocked;
        set
        {
            if (SetField(ref _showBlocked, value))
            {
                Refresh(EditorRefresh.Canvas);
            }
        }
    }

    public int? HoverX
    {
        get => _hoverX;
        set => SetField(ref _hoverX, value);
    }

    public int? HoverY
    {
        get => _hoverY;
        set => SetField(ref _hoverY, value);
    }

    public int? SelectedX
    {
        get => _selectedX;
        set => SetField(ref _selectedX, value);
    }

    public int? SelectedY
    {
        get => _selectedY;
        set => SetField(ref _selectedY, value);
    }

    public int ZoomPercent
    {
        get => _zoomPercent;
        set
        {
            if (value < 25 || value > 400)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetField(ref _zoomPercent, value);
        }
    }

    public int MapWidth => _mapWidth;

    public int MapHeight => _mapHeight;

    public string Title => _title;

    public string TabTitle => _tabTitle;

    public string TabToolTip => _tabToolTip;

    public bool IsDirty => _session.IsDirty;

    public bool CanUndo => _canUndo;

    public bool CanRedo => _canRedo;

    public bool CanSave => _canSave;

    public TileClipboard? Clipboard => _clipboard.Current?.Tiles;

    public bool PasteMode
    {
        get => _pasteMode;
        private set => SetField(ref _pasteMode, value);
    }

    public MapTileRectangle? SelectionRectangle
    {
        get => _selectionRectangle;
        set => SetField(ref _selectionRectangle, value);
    }

    public MapTileRectangle? PasteGhost
    {
        get
        {
            if (!_pasteMode || _clipboard.Current is not { Kind: EditorClipboardKind.Tiles, Tiles: { } clip } || HoverX is not { } x || HoverY is not { } y)
            {
                return null;
            }

            return new MapTileRectangle(x, y, clip.Width, clip.Height);
        }
    }

    public void CopySelection()
    {
        if (_gameData is { ActiveTool: GameDataTool.Spawn or GameDataTool.Warp } state && state.Session is { } session)
        {
            if (state.ActiveTool == GameDataTool.Spawn)
            {
                if (state.SelectedSpawn is { } spawnIndex && spawnIndex < session.Edits.Spawns.Count)
                {
                    NpcSpawnRow row = session.Edits.Spawns[spawnIndex];
                    _clipboard.Current = EditorClipboardPayload.FromSpawn(row.NpcId, session.SpreadsheetId);
                }
                return;
            }

            if (state.SelectedWarp is { } warpIndex && warpIndex < session.Edits.Warps.Count)
            {
                WarpRow row = session.Edits.Warps[warpIndex];
                _clipboard.Current = EditorClipboardPayload.FromWarp(row.WarpId, row.WarpX, row.WarpY, session.SpreadsheetId);
            }
            return;
        }

        if (SelectionRectangle is not { } rect)
        {
            return;
        }

        _clipboard.Current = EditorClipboardPayload.FromTiles(TileClipboard.Capture(_session.Document, _session.SelectedLayers, rect));
    }

    public void CutSelection()
    {
        if (_gameData is { ActiveTool: GameDataTool.Spawn or GameDataTool.Warp } state && state.Session is { } session)
        {
            if (state.ActiveTool == GameDataTool.Spawn)
            {
                if (state.SelectedSpawn is { } spawnIndex && spawnIndex < session.Edits.Spawns.Count)
                {
                    NpcSpawnRow row = session.Edits.Spawns[spawnIndex];
                    _clipboard.Current = EditorClipboardPayload.FromSpawn(row.NpcId, session.SpreadsheetId);
                    state.SelectedSpawn = null;
                    session.Edits.RemoveSpawnAt(spawnIndex);
                }
                return;
            }

            if (state.SelectedWarp is { } warpIndex && warpIndex < session.Edits.Warps.Count)
            {
                WarpRow row = session.Edits.Warps[warpIndex];
                _clipboard.Current = EditorClipboardPayload.FromWarp(row.WarpId, row.WarpX, row.WarpY, session.SpreadsheetId);
                state.SelectedWarp = null;
                session.Edits.RemoveWarpAt(warpIndex);
            }
            return;
        }

        CopySelection();
        DeleteSelection();
    }

    public void DeleteSelection()
    {
        if (SelectionRectangle is not { } rect)
        {
            return;
        }

        MapTileRectangle? target = rect.ClipTo(_session.Document.Width, _session.Document.Height);
        if (target is not { } t)
        {
            return;
        }

        var patch = new MapTileLayer[MapDocument.LayerCount][];
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            if ((_session.SelectedLayers & (1 << layer)) == 0)
            {
                continue;
            }

            // zero-initialized entries are the empty tile (0/0), same as the eraser
            patch[layer] = new MapTileLayer[t.Width * t.Height];
        }

        _session.ApplyLayerPatch(t.X, t.Y, t.Width, t.Height, patch);
        CancelPasteMode();
        Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
    }

    public void BeginPasteMode()
    {
        if (_clipboard.Current is { Kind: EditorClipboardKind.Tiles })
        {
            PasteMode = true;
            Refresh(EditorRefresh.Canvas);
        }
    }

    public void CancelPasteMode()
    {
        if (_pasteMode)
        {
            PasteMode = false;
            Refresh(EditorRefresh.Canvas);
        }
    }

    public void PasteSelection()
    {
        if (_clipboard.Current is { Kind: EditorClipboardKind.Spawn or EditorClipboardKind.Warp } payload)
        {
            PasteGameData(payload);
            return;
        }

        BeginPasteMode();
    }

    public void ApplyPasteAt(int x, int y)
    {
        if (_clipboard.Current is not { Kind: EditorClipboardKind.Tiles, Tiles: { } clip })
        {
            return;
        }

        MapTileRectangle? dest = new MapTileRectangle(x, y, clip.Width, clip.Height)
            .ClipTo(_session.Document.Width, _session.Document.Height);
        if (dest is { } d)
        {
            int sourceX = d.X - x;
            int sourceY = d.Y - y;
            var sub = new MapTileLayer[MapDocument.LayerCount][];
            // captured and selected layers pair up top-to-bottom; unpaired layers are dropped
            var sources = new List<int>();
            var targets = new List<int>();
            for (int layer = MapDocument.LayerCount - 1; layer >= 0; layer--)
            {
                if (clip.Layers[layer] is not null)
                {
                    sources.Add(layer);
                }

                if ((_session.SelectedLayers & (1 << layer)) != 0)
                {
                    targets.Add(layer);
                }
            }

            for (int i = 0; i < Math.Min(sources.Count, targets.Count); i++)
            {
                if (clip.Layers[sources[i]] is not { } data)
                {
                    continue;
                }

                var slice = new MapTileLayer[d.Width * d.Height];
                for (int row = 0; row < d.Height; row++)
                {
                    Array.Copy(data, (sourceY + row) * clip.Width + sourceX, slice, row * d.Width, d.Width);
                }

                sub[targets[i]] = slice;
            }

            _session.ApplyLayerPatch(d.X, d.Y, d.Width, d.Height, sub);
        }

        CancelPasteMode();
        Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
    }

    public Task SaveAsync() => _controller.SaveAsync();

    public Task SaveAsAsync() => _controller.SaveAsAsync();

    public Task<bool> ConfirmCloseAsync() => _controller.ConfirmCloseAsync();

    public bool Undo()
    {
        if (!_timeline.Undo())
        {
            return false;
        }

        Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
        return true;
    }

    public bool Redo()
    {
        if (!_timeline.Redo())
        {
            return false;
        }

        Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
        return true;
    }

    internal void CompleteStroke()
    {
        _session.CompleteStroke();
    }

    internal void CancelStroke()
    {
        _session.CancelStroke();
    }

    public void ResizeMap(MapTileRectangle window)
    {
        MapDocument document = _session.Document;
        bool mapChanges = window.X != 0 || window.Y != 0 || window.Width != document.Width || window.Height != document.Height;
        if (!mapChanges)
        {
            return;
        }

        MapResizePlan plan = PlanResize(window);
        bool sheetChanges = plan.HasPulledData &&
            (!RowsEqual(_sheetSession.Spawns, plan.NewSpawns) || !RowsEqual(_sheetSession.Warps, plan.NewWarps));
        if (sheetChanges)
        {
            _timeline.ApplyCompound(
                session => session.ApplyResize(window),
                session => session.ReplaceAll(plan.NewSpawns, plan.NewWarps));
        }
        else
        {
            _session.ApplyResize(window);
        }

        Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
    }

    // The forward transform is the core-emitted (-X, -Y) offset; a row survives only if its
    // shifted position stays inside the new half-open bounds.
    internal MapResizePlan PlanResize(MapTileRectangle window)
    {
        MapDocument document = _session.Document;
        int croppedTiles = 0;
        for (int y = 0; y < document.Height; y++)
        {
            for (int x = 0; x < document.Width; x++)
            {
                bool inside = x >= window.X && x < window.X + window.Width && y >= window.Y && y < window.Y + window.Height;
                if (!inside && !document[x, y].IsEmpty)
                {
                    croppedTiles++;
                }
            }
        }

        var newSpawns = new List<NpcSpawnRow>();
        var newWarps = new List<WarpRow>();
        int croppedSpawns = 0;
        int croppedWarps = 0;
        int inboundWarps = 0;
        bool pulled = _gameData is { HasSession: true };
        if (pulled)
        {
            int offsetX = -window.X;
            int offsetY = -window.Y;
            int confirmedMapId = _gameData.ConfirmedMapId ?? -1;
            foreach (NpcSpawnRow spawn in _sheetSession.Spawns)
            {
                int x = spawn.MapX + offsetX;
                int y = spawn.MapY + offsetY;
                if (x < 0 || x >= window.Width || y < 0 || y >= window.Height)
                {
                    croppedSpawns++;
                    continue;
                }

                newSpawns.Add(spawn with { MapX = x, MapY = y });
            }

            foreach (WarpRow warp in _sheetSession.Warps)
            {
                int x = warp.MapX + offsetX;
                int y = warp.MapY + offsetY;
                int destinationX = warp.WarpX;
                int destinationY = warp.WarpY;
                bool self = warp.WarpId == confirmedMapId;
                if (self)
                {
                    destinationX += offsetX;
                    destinationY += offsetY;
                }

                if (x < 0 || x >= window.Width || y < 0 || y >= window.Height)
                {
                    croppedWarps++;
                    continue;
                }

                if (self && (destinationX < 0 || destinationX >= window.Width || destinationY < 0 || destinationY >= window.Height))
                {
                    inboundWarps++;
                }

                newWarps.Add(warp with { MapX = x, MapY = y, WarpX = destinationX, WarpY = destinationY });
            }
        }

        return new MapResizePlan(window, croppedTiles, croppedSpawns, croppedWarps, inboundWarps, pulled, newSpawns, newWarps);
    }

    private static bool RowsEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right) where T : struct
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal int? FindSpawnAt(int x, int y)
    {
        if (_gameData?.Session is not { Edits: { } edits })
        {
            return null;
        }

        var spawns = edits.Spawns;
        for (int i = 0; i < spawns.Count; i++)
        {
            if (spawns[i].MapX == x && spawns[i].MapY == y)
            {
                return i;
            }
        }

        return null;
    }

    internal int? FindWarpAt(int x, int y)
    {
        if (_gameData?.Session is not { Edits: { } edits })
        {
            return null;
        }

        var warps = edits.Warps;
        for (int i = 0; i < warps.Count; i++)
        {
            if (warps[i].MapX == x && warps[i].MapY == y)
            {
                return i;
            }
        }

        return null;
    }

    internal void SelectSpawn(int index)
    {
        if (_gameData is not { Session: { Edits: { } edits } } state || index < 0 || index >= edits.Spawns.Count)
        {
            return;
        }

        state.SelectedSpawn = index;
    }

    internal void SelectWarp(int index)
    {
        if (_gameData is not { Session: { Edits: { } edits } } state || index < 0 || index >= edits.Warps.Count)
        {
            return;
        }

        state.SelectedWarp = index;
    }

    internal void AddSpawnAt(int x, int y)
    {
        if (_gameData is not { Session: { } session } state || !InBounds(x, y))
        {
            return;
        }

        if (state.SelectedNpcId is not { } npcId || !session.Npcs.ContainsKey(npcId))
        {
            return;
        }

        int index = session.Edits.Spawns.Count;
        session.Edits.AddSpawn(new NpcSpawnRow(npcId, session.MapId, x, y));
        state.SelectedSpawn = index;
    }

    internal void AddWarpAt(int x, int y)
    {
        if (_gameData is not { Session: { } session } state || !InBounds(x, y))
        {
            return;
        }

        if (state.SelectedDestinationMapId is not { } mapId || !session.Maps.Any(map => map.MapId == mapId))
        {
            return;
        }

        if (state.PendingDestinationX is not { } destinationX || state.PendingDestinationY is not { } destinationY)
        {
            return;
        }

        if (FindWarpAt(x, y) is not null)
        {
            RaiseGameDataError(new ErrorPresentation("Add warp", $"Warp source tile ({x}, {y}) is already defined."));
            return;
        }

        int index = session.Edits.Warps.Count;
        session.Edits.AddWarp(new WarpRow(session.MapId, x, y, mapId, destinationX, destinationY));
        state.SelectedWarp = index;
    }

    internal void MoveSpawnTo(int index, int x, int y)
    {
        if (_gameData?.Session is not { Edits: { } edits } state || index < 0 || index >= edits.Spawns.Count || !InBounds(x, y))
        {
            return;
        }

        edits.MoveSpawn(index, x, y);
    }

    internal void MoveWarpSourceTo(int index, int x, int y)
    {
        if (_gameData?.Session is not { Edits: { } edits } state || index < 0 || index >= edits.Warps.Count || !InBounds(x, y))
        {
            return;
        }

        WarpRow row = edits.Warps[index];
        if (row.MapX == x && row.MapY == y)
        {
            return;
        }

        for (int i = 0; i < edits.Warps.Count; i++)
        {
            if (i != index && edits.Warps[i].MapX == x && edits.Warps[i].MapY == y)
            {
                RaiseGameDataError(new ErrorPresentation("Move warp", $"Warp source tile ({x}, {y}) is already defined."));
                return;
            }
        }

        edits.MoveWarpSource(index, x, y);
    }

    internal void CommitSpawnNpc(int npcId)
    {
        if (_gameData is not { Session: { } session } state)
        {
            return;
        }

        state.SelectedNpcId = npcId;
        if (state.SelectedSpawn is not { } index || index >= session.Edits.Spawns.Count)
        {
            return;
        }

        if (!session.Npcs.ContainsKey(npcId))
        {
            return;
        }

        NpcSpawnRow row = session.Edits.Spawns[index];
        if (row.NpcId == npcId)
        {
            return;
        }

        session.Edits.UpdateSpawn(index, row with { NpcId = npcId });
    }

    internal void CommitSpawnCoordinates(int x, int y)
        => MoveSpawnTo(_gameData?.SelectedSpawn ?? -1, x, y);

    internal void CommitWarpDestinationMap(int mapId)
    {
        if (_gameData is not { Session: { } session } state)
        {
            return;
        }

        state.SelectedDestinationMapId = mapId;
        if (state.SelectedWarp is not { } index || index >= session.Edits.Warps.Count)
        {
            return;
        }

        if (!session.Maps.Any(map => map.MapId == mapId))
        {
            return;
        }

        WarpRow row = session.Edits.Warps[index];
        if (row.WarpId == mapId)
        {
            return;
        }

        session.Edits.UpdateWarp(index, row with { WarpId = mapId });
    }

    internal void CommitWarpSourceCoordinates(int x, int y)
        => MoveWarpSourceTo(_gameData?.SelectedWarp ?? -1, x, y);

    internal void CommitWarpDestinationCoordinates(int x, int y)
    {
        if (_gameData is not { Session: { Edits: { } edits } } state)
        {
            return;
        }

        state.PendingDestinationX = x;
        state.PendingDestinationY = y;
        if (state.SelectedWarp is not { } index || index >= edits.Warps.Count)
        {
            return;
        }

        WarpRow row = edits.Warps[index];
        if (row.WarpX == x && row.WarpY == y)
        {
            return;
        }

        edits.UpdateWarp(index, row with { WarpX = x, WarpY = y });
    }

    internal void RemoveSelectedSpawn()
    {
        if (_gameData is not { Session: { Edits: { } edits } } state || state.SelectedSpawn is not { } index)
        {
            return;
        }

        state.SelectedSpawn = null;
        if (index >= edits.Spawns.Count)
        {
            return;
        }

        edits.RemoveSpawnAt(index);
    }

    internal void RemoveSelectedWarp()
    {
        if (_gameData is not { Session: { Edits: { } edits } } state || state.SelectedWarp is not { } index)
        {
            return;
        }

        state.SelectedWarp = null;
        if (index >= edits.Warps.Count)
        {
            return;
        }

        edits.RemoveWarpAt(index);
    }

    internal void RemoveSelectedGameData()
    {
        if (_gameData?.SelectedSpawn is not null)
        {
            RemoveSelectedSpawn();
        }
        else
        {
            RemoveSelectedWarp();
        }
    }

    private bool InBounds(int x, int y)
    {
        MapDocument document = _session.Document;
        return x >= 0 && x < document.Width && y >= 0 && y < document.Height;
    }

    private void PasteGameData(EditorClipboardPayload payload)
    {
        if (_gameData is not { Session: { } session } state)
        {
            RaiseGameDataError(new ErrorPresentation("Paste game data", "Paste requires a pulled game data session."));
            return;
        }

        if (SelectedX is not { } x || SelectedY is not { } y || !InBounds(x, y))
        {
            RaiseGameDataError(new ErrorPresentation("Paste game data", "Paste requires a selected map tile."));
            return;
        }

        if (!string.Equals(payload.SourceSpreadsheetId, session.SpreadsheetId, StringComparison.Ordinal))
        {
            RaiseGameDataError(new ErrorPresentation("Paste game data", "Paste requires the same spreadsheet."));
            return;
        }

        if (payload.Kind == EditorClipboardKind.Spawn)
        {
            int spawnIndex = session.Edits.Spawns.Count;
            session.Edits.AddSpawn(new NpcSpawnRow(payload.SpawnNpcId!.Value, session.MapId, x, y));
            state.SelectedSpawn = spawnIndex;
            return;
        }

        int index = session.Edits.Warps.Count;
        session.Edits.AddWarp(new WarpRow(session.MapId, x, y, payload.WarpDestinationMapId!.Value, payload.WarpDestinationX!.Value, payload.WarpDestinationY!.Value));
        state.SelectedWarp = index;
    }

    private void RaiseGameDataError(ErrorPresentation error) => GameDataError?.Invoke(error);

    public void Refresh(EditorRefresh flags)
    {
        if (flags.HasFlag(EditorRefresh.Title))
        {
            SetField(ref _title, BuildTitle(), nameof(Title));
            SetField(ref _tabTitle, BuildTabTitle(), nameof(TabTitle));
            SetField(ref _tabToolTip, _controller.Document.Path ?? UntitledName, nameof(TabToolTip));
            SetField(ref _lastNotifiedDirty, _session.IsDirty, nameof(IsDirty));
        }

        if (flags.HasFlag(EditorRefresh.Commands))
        {
            SetField(ref _canUndo, _timeline.CanUndo, nameof(CanUndo));
            SetField(ref _canRedo, _timeline.CanRedo, nameof(CanRedo));
            SetField(ref _canSave, _session.IsDirty, nameof(CanSave));
            SetField(ref _lastNotifiedDirty, _session.IsDirty, nameof(IsDirty));
        }

        if (flags.HasFlag(EditorRefresh.Canvas))
        {
            CanvasInvalidated?.Invoke();
        }

        if (flags.HasFlag(EditorRefresh.Palette))
        {
            PaletteInvalidated?.Invoke();
        }
    }

    private void OnControllerStateChanged()
    {
        Refresh(EditorRefresh.Title | EditorRefresh.Commands);
    }

    private void OnGameDataChanged()
    {
        Refresh(EditorRefresh.Title | EditorRefresh.Commands);
    }

    private void OnClipboardChanged()
    {
        OnPropertyChanged(nameof(Clipboard));
        Refresh(EditorRefresh.Commands);
    }

    private void OnSessionResized(MapResizeTransform transform)
    {
        SetField(ref _mapWidth, transform.Width, nameof(MapWidth));
        SetField(ref _mapHeight, transform.Height, nameof(MapHeight));
        (SelectedX, SelectedY) = ShiftPoint(SelectedX, SelectedY, transform);
        HoverX = null;
        HoverY = null;
        SelectionRectangle = ShiftRect(SelectionRectangle, transform);
        CancelPasteMode();
        if (_gameData is not null)
        {
            _gameData.SelectedSpawn = null;
            _gameData.SelectedWarp = null;
        }
    }

    private static (int? X, int? Y) ShiftPoint(int? x, int? y, in MapResizeTransform transform)
    {
        if (x is not { } xValue || y is not { } yValue)
        {
            return (null, null);
        }

        int shiftedX = xValue + transform.OffsetX;
        int shiftedY = yValue + transform.OffsetY;
        if (shiftedX < 0 || shiftedX >= transform.Width || shiftedY < 0 || shiftedY >= transform.Height)
        {
            return (null, null);
        }

        return (shiftedX, shiftedY);
    }

    private static MapTileRectangle? ShiftRect(MapTileRectangle? rect, in MapResizeTransform transform)
    {
        if (rect is not { } value)
        {
            return null;
        }

        return new MapTileRectangle(value.X + transform.OffsetX, value.Y + transform.OffsetY, value.Width, value.Height)
            .ClipTo(transform.Width, transform.Height);
    }

    private string BuildTabTitle()
        => _controller.Document.Path is { } path ? Path.GetFileName(path) : UntitledName;

    private string BuildTitle()
    {
        string name = _controller.Document.Path is { } path ? Path.GetFileName(path) : UntitledName;
        return $"Goose2 Map Editor — {name}{(_session.IsDirty ? "*" : string.Empty)}";
    }
}
