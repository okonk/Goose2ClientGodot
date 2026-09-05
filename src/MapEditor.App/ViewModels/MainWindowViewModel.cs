using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MapEditor.App.Documents;
using MapEditor.Core;

namespace MapEditor.App.ViewModels;

[Flags]
internal enum EditorRefresh
{
    None = 0,
    Title = 1 << 0,
    Commands = 1 << 1,
    Canvas = 1 << 2,
    Palette = 1 << 3,
    Document = 1 << 4
}

internal sealed class MainWindowViewModel : ViewModelBase
{
    private const string UntitledName = "Untitled";

    private readonly EditorDocumentController _controller;
    private MapEditSession _session;

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
    private TileClipboard? _clipboard;
    private bool _pasteMode;
    private MapTileRectangle? _selectionRectangle;
    private int _zoomPercent = 100;
    private int _mapWidth;
    private int _mapHeight;
    private string _title;
    private bool _canUndo;
    private bool _canRedo;
    private bool _canSave;

    public MainWindowViewModel(EditorDocumentController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _session = _controller.Document.Session;
        _mapWidth = _session.Document.Width;
        _mapHeight = _session.Document.Height;
        _title = BuildTitle();
        _canSave = _session.IsDirty;
        _controller.StateChanged += OnControllerStateChanged;
        _session.Resized += OnSessionResized;
    }

    public event Action? CanvasInvalidated;

    public event Action? PaletteInvalidated;

    public MapEditSession Session => _controller.Document.Session;

    public MapEditTool ActiveTool
    {
        get => _activeTool;
        set
        {
            if (value < MapEditTool.Pencil || value > MapEditTool.FloodFill)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
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

    public bool CanUndo => _canUndo;

    public bool CanRedo => _canRedo;

    public bool CanSave => _canSave;

    public TileClipboard? Clipboard => _clipboard;

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
            if (!_pasteMode || _clipboard is not { } clip || HoverX is not { } x || HoverY is not { } y)
            {
                return null;
            }

            return new MapTileRectangle(x, y, clip.Width, clip.Height);
        }
    }

    public void CopySelection()
    {
        if (SelectionRectangle is not { } rect)
        {
            return;
        }

        _clipboard = TileClipboard.Capture(_session.Document, _session.SelectedLayers, rect);
    }

    public void CutSelection()
    {
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
        if (_clipboard is not null)
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

    public void ApplyPasteAt(int x, int y)
    {
        TileClipboard? clip = _clipboard;
        if (clip is null)
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

    public Task NewAsync() => _controller.NewAsync();

    public Task OpenAsync() => _controller.OpenAsync();

    public Task SaveAsync() => _controller.SaveAsync();

    public Task SaveAsAsync() => _controller.SaveAsAsync();

    public Task<bool> RequestCloseAsync() => _controller.RequestCloseAsync();

    public bool Undo()
    {
        if (!_controller.Undo())
        {
            return false;
        }

        Refresh(EditorRefresh.Canvas);
        return true;
    }

    public bool Redo()
    {
        if (!_controller.Redo())
        {
            return false;
        }

        Refresh(EditorRefresh.Canvas);
        return true;
    }

    public void ResizeMap(MapTileRectangle window)
    {
        if (!_session.ApplyResize(window))
        {
            return;
        }

        Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
    }

    public void Refresh(EditorRefresh flags)
    {
        bool documentReplaced = false;
        if (flags.HasFlag(EditorRefresh.Document))
        {
            MapEditSession session = _controller.Document.Session;
            if (!ReferenceEquals(session, _session))
            {
                _session.Resized -= OnSessionResized;
                documentReplaced = true;
                _session = session;
                _session.Resized += OnSessionResized;
                SetField(ref _mapWidth, session.Document.Width, nameof(MapWidth));
                SetField(ref _mapHeight, session.Document.Height, nameof(MapHeight));
                HoverX = null;
                HoverY = null;
                SelectedX = null;
                SelectedY = null;
                _clipboard = null;
                CancelPasteMode();
                SelectionRectangle = null;
                OnPropertyChanged(nameof(SelectedLayers));
                OnPropertyChanged(nameof(Brush));
            }
        }

        if (flags.HasFlag(EditorRefresh.Title))
        {
            SetField(ref _title, BuildTitle(), nameof(Title));
        }

        if (flags.HasFlag(EditorRefresh.Commands))
        {
            SetField(ref _canUndo, _session.CanUndo, nameof(CanUndo));
            SetField(ref _canRedo, _session.CanRedo, nameof(CanRedo));
            SetField(ref _canSave, _session.IsDirty, nameof(CanSave));
        }

        if (documentReplaced || flags.HasFlag(EditorRefresh.Canvas))
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
        Refresh(EditorRefresh.Document | EditorRefresh.Title | EditorRefresh.Commands);
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

    private string BuildTitle()
    {
        string name = _controller.Document.Path is { } path ? Path.GetFileName(path) : UntitledName;
        return $"Goose2 Map Editor — {name}{(_session.IsDirty ? "*" : string.Empty)}";
    }
}
