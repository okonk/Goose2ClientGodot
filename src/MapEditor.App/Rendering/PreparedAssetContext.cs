using System;
using System.Collections.Generic;
using System.ComponentModel;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class PreparedAssetContext : IDisposable
{
    private readonly AssetContext _context;
    private bool _consumed;
    private bool _disposed;

    internal PreparedAssetContext(AssetContext context, string fullPath)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        FullPath = fullPath ?? throw new ArgumentNullException(nameof(fullPath));
    }

    internal AssetContext Context => _context;

    internal string FullPath { get; }

    internal bool IsDisposed => _disposed;

    internal void MarkConsumed() => _consumed = true;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_consumed)
        {
            _context.Dispose();
        }
    }
}

internal interface IPreparedAssetReconciliation
{
    void Apply();

    void Notify();
}

internal sealed class TerrainPublication
{
    public TerrainCatalogLoadResult? Result { get; internal set; }

    // A prepared save cannot know its durable revision until Commit, so the document
    // plans its choices and selection fallback from the prepared catalog/index instead.
    internal TerrainCatalog? PreparedCatalog { get; set; }

    internal TerrainCatalogIndex? PreparedIndex { get; set; }
}

internal sealed class TerrainDocumentReconciliation
{
    private readonly MapDocumentViewModel _document;
    private readonly IReadOnlyList<int> _sheetIds;
    private readonly int _selectedSheet;
    private readonly Func<TerrainCatalogLoadResult> _terrain;
    private readonly IReadOnlyList<TerrainChoice> _choices;
    private readonly Guid? _selectedTerrainId;
    private readonly MapEditTool _activeTool;
    private readonly PropertyChangedEventArgs[] _propertyArguments;
    private readonly Delegate[] _propertyHandlers;
    private readonly Delegate[] _canvasHandlers;
    private readonly Delegate[] _paletteHandlers;

    internal TerrainDocumentReconciliation(
        MapDocumentViewModel document,
        IReadOnlyList<int> sheetIds,
        int selectedSheet,
        Func<TerrainCatalogLoadResult> terrain,
        IReadOnlyList<TerrainChoice> choices,
        Guid? selectedTerrainId,
        MapEditTool activeTool,
        PropertyChangedEventArgs[] propertyArguments,
        Delegate[] propertyHandlers,
        Delegate[] canvasHandlers,
        Delegate[] paletteHandlers)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _sheetIds = sheetIds ?? throw new ArgumentNullException(nameof(sheetIds));
        _selectedSheet = selectedSheet;
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _choices = choices ?? throw new ArgumentNullException(nameof(choices));
        _selectedTerrainId = selectedTerrainId;
        _activeTool = activeTool;
        _propertyArguments = propertyArguments ?? throw new ArgumentNullException(nameof(propertyArguments));
        _propertyHandlers = propertyHandlers ?? throw new ArgumentNullException(nameof(propertyHandlers));
        _canvasHandlers = canvasHandlers ?? throw new ArgumentNullException(nameof(canvasHandlers));
        _paletteHandlers = paletteHandlers ?? throw new ArgumentNullException(nameof(paletteHandlers));
    }

    internal IReadOnlyList<int> SheetIds => _sheetIds;

    internal int SelectedSheet => _selectedSheet;

    internal TerrainCatalogLoadResult Terrain => _terrain();

    internal IReadOnlyList<TerrainChoice> Choices => _choices;

    internal Guid? SelectedTerrainId => _selectedTerrainId;

    internal MapEditTool ActiveTool => _activeTool;

    internal PropertyChangedEventArgs[] PropertyArguments => _propertyArguments;

    internal Delegate[] PropertyHandlers => _propertyHandlers;

    internal Delegate[] CanvasHandlers => _canvasHandlers;

    internal Delegate[] PaletteHandlers => _paletteHandlers;

    internal void Apply() => _document.ApplyTerrainReconciliation(this);

    internal void Notify(PublicationNotificationErrors errors) => _document.NotifyTerrainReconciliation(this, errors);
}

internal interface ITerrainDocumentReconciler
{
    TerrainDocumentReconciliation PrepareRootPublication(AssetContext context);

    TerrainDocumentReconciliation PrepareTerrainPublication(TerrainPublication publication);
}

internal sealed class PublicationNotificationErrors
{
    private readonly Exception[] _buffer;
    private int _count;

    internal PublicationNotificationErrors(int capacity)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _buffer = new Exception[capacity];
    }

    public int Count => _count;

    public Exception this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _buffer[index];
        }
    }

    internal bool TryAdd(Exception exception)
    {
        if (_count >= _buffer.Length)
        {
            return false;
        }

        _buffer[_count++] = exception;
        return true;
    }
}

internal sealed class TerrainCatalogChangedEventArgs : EventArgs
{
    private readonly TerrainPublication _publication;

    internal TerrainCatalogChangedEventArgs(TerrainPublication publication)
    {
        _publication = publication ?? throw new ArgumentNullException(nameof(publication));
    }

    public TerrainCatalogLoadResult? Terrain => _publication.Result;
}
