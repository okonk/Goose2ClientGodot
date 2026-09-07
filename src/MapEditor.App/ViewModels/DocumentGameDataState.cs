using System;
using MapEditor.App.Connectivity;
using MapEditor.GameData.Editing;
using MapEditor.GameData.Sync;

namespace MapEditor.App.ViewModels;

internal sealed class DocumentGameDataState : IDisposable
{
    private readonly MapDocumentViewModel _document;
    private GameDataSyncSession? _session;
    private SheetEditSession? _subscribedEdits;
    private GameDataCommandController? _controller;
    private bool _disposed;

    internal DocumentGameDataState(MapDocumentViewModel document, GameDataCommandController controller)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _controller.StateChanged += OnControllerStateChanged;
    }

    public GameDataSyncSession? Session => _session;

    public bool HasSession => _session is not null;

    public bool IsDirty => _session?.Edits.IsDirty ?? false;

    public bool RequiresPull => _session?.RequiresPull ?? false;

    public string? SpreadsheetId => _session?.SpreadsheetId;

    public int? ConfirmedMapId => _session?.MapId;

    public event Action? Changed;

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

    private void RaiseChanged()
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }
}
