using System;
using System.ComponentModel;
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
}

internal sealed class TerrainDocumentReconciliation
{
    private readonly Action _apply;
    private readonly Action _notifyProperty;
    private readonly Action _notifyCanvas;
    private readonly Action _notifyPalette;

    internal TerrainDocumentReconciliation(Action apply, Action notifyProperty, Action notifyCanvas, Action notifyPalette)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _notifyProperty = notifyProperty ?? throw new ArgumentNullException(nameof(notifyProperty));
        _notifyCanvas = notifyCanvas ?? throw new ArgumentNullException(nameof(notifyCanvas));
        _notifyPalette = notifyPalette ?? throw new ArgumentNullException(nameof(notifyPalette));
    }

    internal void Apply() => _apply();

    internal void NotifyProperty() => _notifyProperty();

    internal void NotifyCanvas() => _notifyCanvas();

    internal void NotifyPalette() => _notifyPalette();
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

    internal void Add(Exception exception) => _buffer[_count++] = exception;
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
