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
    private bool _layer0Visible = true;
    private bool _layer1Visible = true;
    private bool _layer2Visible = true;
    private bool _layer3Visible = true;
    private bool _layer4Visible = true;
    private bool _showGrid = true;
    private bool _showBlocked;
    private int? _hoverX;
    private int? _hoverY;
    private int? _selectedX;
    private int? _selectedY;
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
    }

    public event Action? CanvasInvalidated;

    public event Action? PaletteInvalidated;

    public MapEditSession Session => _controller.Document.Session;

    public MapEditTool ActiveTool
    {
        get => _activeTool;
        set
        {
            if (value < MapEditTool.Pencil || value > MapEditTool.BlockedToggle)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetField(ref _activeTool, value);
        }
    }

    public int ActiveLayer
    {
        get => _session.ActiveLayer;
        set
        {
            if (value < 0 || value >= MapDocument.LayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_session.ActiveLayer != value)
            {
                _session.ActiveLayer = value;
                OnPropertyChanged();
            }
        }
    }

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

    public bool Layer0Visible
    {
        get => _layer0Visible;
        set => SetField(ref _layer0Visible, value);
    }

    public bool Layer1Visible
    {
        get => _layer1Visible;
        set => SetField(ref _layer1Visible, value);
    }

    public bool Layer2Visible
    {
        get => _layer2Visible;
        set => SetField(ref _layer2Visible, value);
    }

    public bool Layer3Visible
    {
        get => _layer3Visible;
        set => SetField(ref _layer3Visible, value);
    }

    public bool Layer4Visible
    {
        get => _layer4Visible;
        set => SetField(ref _layer4Visible, value);
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set => SetField(ref _showGrid, value);
    }

    public bool ShowBlocked
    {
        get => _showBlocked;
        set => SetField(ref _showBlocked, value);
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

    public void Refresh(EditorRefresh flags)
    {
        bool documentReplaced = false;
        if (flags.HasFlag(EditorRefresh.Document))
        {
            MapEditSession session = _controller.Document.Session;
            if (!ReferenceEquals(session, _session))
            {
                documentReplaced = true;
                _session = session;
                SetField(ref _mapWidth, session.Document.Width);
                SetField(ref _mapHeight, session.Document.Height);
                HoverX = null;
                HoverY = null;
                SelectedX = null;
                SelectedY = null;
                OnPropertyChanged(nameof(ActiveLayer));
                OnPropertyChanged(nameof(Brush));
            }
        }

        if (flags.HasFlag(EditorRefresh.Title))
        {
            SetField(ref _title, BuildTitle());
        }

        if (flags.HasFlag(EditorRefresh.Commands))
        {
            SetField(ref _canUndo, _session.CanUndo);
            SetField(ref _canRedo, _session.CanRedo);
            SetField(ref _canSave, _session.IsDirty);
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

    private string BuildTitle()
    {
        string name = _controller.Document.Path is { } path ? Path.GetFileName(path) : UntitledName;
        return $"Goose2 Map Editor — {name}{(_session.IsDirty ? "*" : string.Empty)}";
    }
}
