using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MapEditor.App.Connectivity;
using MapEditor.GameData.Editing;
using MapEditor.GameData.Sync;

namespace MapEditor.App.ViewModels;

internal enum GameDataTool
{
    None,
    Spawn,
    Warp
}

internal sealed class DocumentGameDataState : IDisposable, INotifyPropertyChanged
{
    private readonly MapDocumentViewModel _document;
    private GameDataSyncSession? _session;
    private SheetEditSession? _subscribedEdits;
    private GameDataCommandController? _controller;
    private bool _disposed;
    private GameDataTool _activeTool;
    private bool _showSpawnOverlay = true;
    private bool _showWarpOverlay = true;
    private bool _previewMode;
    private int? _selectedSpawn;
    private int? _selectedWarp;

    internal DocumentGameDataState(MapDocumentViewModel document, GameDataCommandController controller)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _controller.StateChanged += OnControllerStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? Changed;

    public GameDataSyncSession? Session => _session;

    public bool HasSession => _session is not null;

    public bool IsDirty => _session?.Edits.IsDirty ?? false;

    public bool RequiresPull => _session?.RequiresPull ?? false;

    public string? SpreadsheetId => _session?.SpreadsheetId;

    public int? ConfirmedMapId => _session?.MapId;

    public GameDataTool ActiveTool
    {
        get => _activeTool;
        set => SetField(ref _activeTool, value);
    }

    public bool ShowSpawnOverlay
    {
        get => _showSpawnOverlay;
        set => SetField(ref _showSpawnOverlay, value);
    }

    public bool ShowWarpOverlay
    {
        get => _showWarpOverlay;
        set => SetField(ref _showWarpOverlay, value);
    }

    public bool PreviewMode
    {
        get => _previewMode;
        set => SetField(ref _previewMode, value);
    }

    public int? SelectedSpawn
    {
        get => _selectedSpawn;
        set
        {
            if (!SetField(ref _selectedSpawn, value))
            {
                return;
            }

            if (value is not null)
            {
                SetField(ref _selectedWarp, null);
            }

            RaiseChanged();
        }
    }

    public int? SelectedWarp
    {
        get => _selectedWarp;
        set
        {
            if (!SetField(ref _selectedWarp, value))
            {
                return;
            }

            if (value is not null)
            {
                SetField(ref _selectedSpawn, null);
            }

            RaiseChanged();
        }
    }

    internal void AttachSession(GameDataSyncSession session)
    {
        if (_disposed)
        {
            return;
        }

        DetachEdits();
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _subscribedEdits = _session.Edits;
        _subscribedEdits.HistoryChanged += OnEditsHistoryChanged;
        _document.AttachSheetSession(_subscribedEdits);
        _selectedSpawn = null;
        _selectedWarp = null;
        RaiseChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachEdits();
        if (_controller is not null)
        {
            _controller.StateChanged -= OnControllerStateChanged;
            _controller = null;
        }
    }

    private void DetachEdits()
    {
        if (_subscribedEdits is not null)
        {
            _subscribedEdits.HistoryChanged -= OnEditsHistoryChanged;
            _subscribedEdits = null;
        }
    }

    private void OnEditsHistoryChanged() => RaiseChanged();

    private void OnControllerStateChanged() => RaiseChanged();

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        RaiseChanged();
        return true;
    }

    private void RaiseChanged()
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }
}
