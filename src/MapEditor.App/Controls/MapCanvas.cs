using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using MapEditor.App.Rendering;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Controls;

internal sealed class MapCanvas : Control, ICustomHitTest
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AssetContextController _assets;
    private MapEditSession _trackedSession;
    private ViewportTransform _viewport = new(new RenderSize(1, 1), new RenderPoint(0, 0), MapZoom.Percent100);
    private bool _stroking;
    private bool _panning;
    private bool _spaceDown;
    private Point _lastPanPosition;
    private Window? _hoverWindow;

    public MapCanvas(MainWindowViewModel viewModel, AssetContextController assets)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _trackedSession = _viewModel.Session;
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        _viewModel.CanvasInvalidated += OnCanvasInvalidated;
        SizeChanged += OnSizeChanged;
    }

    internal ViewportTransform Viewport => _viewport;

    bool ICustomHitTest.HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public void FinishInteraction(bool commit)
    {
        MapEditSession session = _viewModel.Session;
        if (session.HasActiveStroke)
        {
            if (commit)
            {
                session.CompleteStroke();
            }
            else
            {
                session.CancelStroke();
            }
        }

        _stroking = false;
        _panning = false;
        _viewModel.Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
    }

    internal void ZoomStep(bool zoomIn)
    {
        MapZoom next = zoomIn ? MapZoomLevels.ZoomIn(_viewport.Zoom) : MapZoomLevels.ZoomOut(_viewport.Zoom);
        if (next != _viewport.Zoom)
        {
            Point center = new(Bounds.Width / 2, Bounds.Height / 2);
            _viewport = _viewport.ZoomAt(next, new RenderPoint(center.X, center.Y));
            _viewModel.ZoomPercent = (int)next;
            Invalidate();
        }
    }

    internal void RenderMap(IMapDrawTarget target)
    {
        using IDisposable clip = target.PushClip(new Rect(Bounds.Size));
        _assets.Current.Renderer.Render(BuildRenderRequest(), new AvaloniaMapDrawSink(target));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RenderMap(new DrawingContextMapDrawTarget(context));
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _viewport = new ViewportTransform(new RenderSize(Bounds.Width, Bounds.Height), _viewport.WorldOrigin, _viewport.Zoom);
        Invalidate();
    }

    internal int InvalidationCount { get; private set; }

    private void Invalidate()
    {
        InvalidationCount++;
        InvalidateVisual();
    }

    internal bool IsGestureActive => _stroking || _panning;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsGestureActive)
        {
            e.Handled = true;
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        PointerPointProperties properties = point.Properties;
        if (properties.IsLeftButtonPressed)
        {
            if (_spaceDown)
            {
                BeginPan(point.Position);
            }
            else
            {
                BeginStroke(point.Position);
            }
        }
        else if (properties.IsMiddleButtonPressed)
        {
            BeginPan(point.Position);
        }

        if (_stroking || _panning)
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (VisualRoot is Window window && !ReferenceEquals(_hoverWindow, window))
        {
            _hoverWindow = window;
            window.PointerMoved += OnWindowPointerMoved;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_hoverWindow is { } window)
        {
            window.PointerMoved -= OnWindowPointerMoved;
            _hoverWindow = null;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        PointerPoint point = e.GetCurrentPoint(this);
        Point position = point.Position;
        UpdateHover(position);

        if (_panning)
        {
            _viewport = _viewport.PanByScreenDelta(new RenderPoint(position.X - _lastPanPosition.X, position.Y - _lastPanPosition.Y));
            _lastPanPosition = position;
            Invalidate();
            return;
        }

        if (_stroking)
        {
            MapTileCoordinate? tile = TileAt(position);
            if (tile is { } target)
            {
                MapEditSession session = _viewModel.Session;
                session.ContinueStroke(target.X, target.Y);
                _viewModel.Brush = session.SelectedTileLayer;
                _viewModel.Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
            }
        }
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        UpdateHover(e.GetPosition(this));
    }

    private void UpdateHover(Point position)
    {
        MapTileCoordinate? tile = Bounds.Contains(position) ? TileAt(position) : null;
        int? hoverX = null;
        int? hoverY = null;
        if (tile is { } hovered)
        {
            hoverX = hovered.X;
            hoverY = hovered.Y;
        }
        if (hoverX == _viewModel.HoverX && hoverY == _viewModel.HoverY)
        {
            return;
        }

        _viewModel.HoverX = hoverX;
        _viewModel.HoverY = hoverY;
        Invalidate();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_stroking && !_panning)
        {
            return;
        }

        if (_stroking)
        {
            _viewModel.Session.CompleteStroke();
            _stroking = false;
        }

        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        _viewModel.Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_stroking && !_panning)
        {
            return;
        }

        if (_stroking)
        {
            _viewModel.Session.CompleteStroke();
        }

        _stroking = false;
        _panning = false;
        _viewModel.Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double delta = e.Delta.Y;
        if (delta == 0)
        {
            return;
        }

        MapZoom next = delta > 0 ? MapZoomLevels.ZoomIn(_viewport.Zoom) : MapZoomLevels.ZoomOut(_viewport.Zoom);
        if (next != _viewport.Zoom)
        {
            Point position = e.GetCurrentPoint(this).Position;
            _viewport = _viewport.ZoomAt(next, new RenderPoint(position.X, position.Y));
            _viewModel.ZoomPercent = (int)next;
            Invalidate();
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            FinishInteraction(commit: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            _spaceDown = true;
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceDown = false;
            e.Handled = true;
        }
    }

    private void BeginStroke(Point position)
    {
        MapTileCoordinate? tile = TileAt(position);
        if (tile is not { } cell)
        {
            return;
        }

        MapEditSession session = _viewModel.Session;
        session.BeginStroke(_viewModel.ActiveTool, cell.X, cell.Y);
        _stroking = true;
        _viewModel.SelectedX = cell.X;
        _viewModel.SelectedY = cell.Y;
        _viewModel.Brush = session.SelectedTileLayer;
        _viewModel.Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
    }

    private void BeginPan(Point position)
    {
        _panning = true;
        _lastPanPosition = position;
    }

    private MapTileCoordinate? TileAt(Point position)
    {
        MapEditSession session = _viewModel.Session;
        MapTileCoordinate tile = _viewport.ScreenToTile(new RenderPoint(position.X, position.Y));
        if (tile.X < 0 || tile.X >= session.Document.Width || tile.Y < 0 || tile.Y >= session.Document.Height)
        {
            return null;
        }

        return tile;
    }

    private void OnCanvasInvalidated()
    {
        MapEditSession session = _viewModel.Session;
        if (!ReferenceEquals(session, _trackedSession))
        {
            _trackedSession = session;
            _viewport = new ViewportTransform(new RenderSize(Bounds.Width, Bounds.Height), new RenderPoint(0, 0), MapZoom.Percent100);
            _viewModel.ZoomPercent = 100;
            _stroking = false;
            _panning = false;
            _spaceDown = false;
        }

        Invalidate();
    }

    private MapRenderRequest BuildRenderRequest()
    {
        MapEditSession session = _viewModel.Session;
        byte mask = 0;
        if (_viewModel.Layer0Visible)
        {
            mask |= 1;
        }

        if (_viewModel.Layer1Visible)
        {
            mask |= 1 << 1;
        }

        if (_viewModel.Layer2Visible)
        {
            mask |= 1 << 2;
        }

        if (_viewModel.Layer3Visible)
        {
            mask |= 1 << 3;
        }

        if (_viewModel.Layer4Visible)
        {
            mask |= 1 << 4;
        }

        MapTileCoordinate? hovered = _viewModel.HoverX is { } hoverX && _viewModel.HoverY is { } hoverY
            ? new MapTileCoordinate(hoverX, hoverY)
            : null;
        MapTileCoordinate? selected = _viewModel.SelectedX is { } selectedX && _viewModel.SelectedY is { } selectedY
            ? new MapTileCoordinate(selectedX, selectedY)
            : null;
        MapRenderOptions options = new(
            new MapLayerVisibility(mask),
            _viewModel.ShowGrid,
            _viewModel.ShowBlocked,
            hovered,
            selected);
        return new MapRenderRequest(session.Document, _viewport, options);
    }
}
