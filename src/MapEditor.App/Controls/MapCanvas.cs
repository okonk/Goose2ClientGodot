using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using MapEditor.App.Rendering;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Rows;
using MapEditor.Rendering;

namespace MapEditor.App.Controls;

internal sealed class MapCanvas : Control, ICustomHitTest
{
    private readonly MapDocumentViewModel _viewModel;
    private readonly AssetContextController _assets;
    private ViewportTransform _viewport = new(new RenderSize(1, 1), new RenderPoint(0, 0), MapZoom.Percent100);
    private bool _stroking;
    private bool _panning;
    private bool _spaceDown;
    private RectDrag? _rectDrag;
    private MarkerDrag? _markerDrag;
    private IPointer? _capturedPointer;
    private Point _lastPanPosition;
    private Window? _hoverWindow;

    private readonly record struct MarkerDrag(GameDataMarkerKind Kind, int Index, MapTileCoordinate Current);

    public MapCanvas(MapDocumentViewModel viewModel, AssetContextController assets)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        _viewModel.CanvasInvalidated += OnCanvasInvalidated;
        _viewModel.GameData?.Changed += OnGameDataChanged;
        SizeChanged += OnSizeChanged;
    }

    internal ViewportTransform Viewport => _viewport;

    bool ICustomHitTest.HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public void FinishInteraction(bool commit)
    {
        if (_viewModel.Session.HasActiveStroke)
        {
            if (commit)
            {
                _viewModel.CompleteStroke();
            }
            else
            {
                _viewModel.CancelStroke();
            }
        }

        _stroking = false;
        _panning = false;
        _spaceDown = false;
        if (_rectDrag is not null)
        {
            if (commit)
            {
                CommitRectDrag();
            }
            else
            {
                CancelRectDrag();
            }
        }

        if (_markerDrag is not null)
        {
            if (commit)
            {
                CommitMarkerDrag();
            }
            else
            {
                CancelMarkerDrag();
            }
        }

        _capturedPointer?.Capture(null);
        _capturedPointer = null;
        _viewModel.CancelPasteMode();
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
        AssetContext context = _assets.Current;
        using IDisposable clip = target.PushClip(new Rect(Bounds.Size));
        context.Renderer.Render(BuildRenderRequest(), new AvaloniaMapDrawSink(target, context.TintCache));
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

    internal bool IsGestureActive => _stroking || _panning || _rectDrag is not null || _markerDrag is not null;

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
            else if (_viewModel.PasteMode)
            {
                MapTileCoordinate? tile = TileAt(point.Position);
                if (tile is { } target)
                {
                    _viewModel.ApplyPasteAt(target.X, target.Y);
                }
                else
                {
                    _viewModel.CancelPasteMode();
                }

                e.Handled = true;
            }
            else
            {
                BeginToolPress(point.Position, e.KeyModifiers);
            }
        }
        else if (properties.IsMiddleButtonPressed)
        {
            BeginPan(point.Position);
        }

        if (IsGestureActive)
        {
            _capturedPointer = e.Pointer;
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

        if (_rectDrag is { } drag)
        {
            MapEditSession session = _viewModel.Session;
            MapTileCoordinate current = _viewport.ScreenToTile(new RenderPoint(position.X, position.Y));
            int x = Math.Clamp(current.X, 0, session.Document.Width - 1);
            int y = Math.Clamp(current.Y, 0, session.Document.Height - 1);
            MapTileCoordinate origin = drag.Origin;
            MapTileRectangle rect = new(
                Math.Min(origin.X, x),
                Math.Min(origin.Y, y),
                Math.Abs(x - origin.X) + 1,
                Math.Abs(y - origin.Y) + 1);
            if (drag.Purpose == RectDragPurpose.Select)
            {
                _viewModel.SelectionRectangle = rect;
            }

            _rectDrag = drag with { Current = rect };
            Invalidate();
            return;
        }

        if (_markerDrag is { } markerDrag)
        {
            if (TileAt(position) is { } markerTile)
            {
                _markerDrag = markerDrag with { Current = markerTile };
                Invalidate();
            }

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
        if (!IsGestureActive)
        {
            return;
        }

        if (_stroking)
        {
            _viewModel.CompleteStroke();
            _stroking = false;
        }

        CommitMarkerDrag();
        CommitRectDrag();
        _panning = false;
        e.Pointer.Capture(null);
        _capturedPointer = null;
        e.Handled = true;
        _viewModel.Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!IsGestureActive)
        {
            return;
        }

        if (_stroking)
        {
            _viewModel.CompleteStroke();
        }

        CancelMarkerDrag();
        CancelRectDrag();
        _stroking = false;
        _panning = false;
        _capturedPointer = null;
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
            _viewModel.CancelPasteMode();
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

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _spaceDown = false;
    }

    private void BeginToolPress(Point position, KeyModifiers modifiers)
    {
        DocumentGameDataState? gameData = _viewModel.GameData;
        if (gameData is { ActiveTool: GameDataTool.Spawn or GameDataTool.Warp })
        {
            BeginGameDataPress(position, gameData);
            return;
        }

        switch (_viewModel.ActiveTool)
        {
            case MapEditTool.Select:
                if (TileAt(position) is { } selected)
                {
                    _viewModel.SelectedX = selected.X;
                    _viewModel.SelectedY = selected.Y;
                    _viewModel.Refresh(EditorRefresh.Canvas);
                }

                break;
            case MapEditTool.MultiSelect:
                if (TileAt(position) is { } origin)
                {
                    _rectDrag = new RectDrag(RectDragPurpose.Select, origin, new MapTileRectangle(origin.X, origin.Y, 1, 1));
                }

                break;
            case MapEditTool.Blocked:
                if (TileAt(position) is { } blockedOrigin)
                {
                    RectDragPurpose purpose = modifiers.HasFlag(KeyModifiers.Shift) ? RectDragPurpose.Unblock : RectDragPurpose.Block;
                    _rectDrag = new RectDrag(purpose, blockedOrigin, new MapTileRectangle(blockedOrigin.X, blockedOrigin.Y, 1, 1));
                    _viewModel.ShowBlocked = true;
                }

                break;
            case MapEditTool.FloodFill:
                if (TileAt(position) is { } cell)
                {
                    _viewModel.Session.ApplyFloodFill(cell.X, cell.Y);
                    _viewModel.SelectedX = cell.X;
                    _viewModel.SelectedY = cell.Y;
                    _viewModel.Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
                }

                break;
            default:
                BeginStroke(position);
                break;
        }
    }

    private void CommitRectDrag()
    {
        if (_rectDrag is not { } drag)
        {
            return;
        }

        switch (drag.Purpose)
        {
            case RectDragPurpose.Block:
            case RectDragPurpose.Unblock:
                _viewModel.Session.ApplyBlockedPatch(drag.Current, drag.Purpose == RectDragPurpose.Block);
                break;
            default:
                _viewModel.SelectionRectangle = drag.Current;
                break;
        }

        _rectDrag = null;
        Invalidate();
        _viewModel.Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
    }

    private void CancelRectDrag()
    {
        if (_rectDrag is null)
        {
            return;
        }

        _rectDrag = null;
        Invalidate();
    }

    private void BeginGameDataPress(Point position, DocumentGameDataState state)
    {
        MapTileCoordinate? tile = TileAt(position);
        if (tile is not { } target)
        {
            return;
        }

        bool spawnTool = state.ActiveTool == GameDataTool.Spawn;
        int? index = spawnTool ? _viewModel.FindSpawnAt(target.X, target.Y) : _viewModel.FindWarpAt(target.X, target.Y);
        if (index is { } hit)
        {
            if (spawnTool)
            {
                _viewModel.SelectSpawn(hit);
                _markerDrag = new MarkerDrag(GameDataMarkerKind.Spawn, hit, target);
            }
            else
            {
                _viewModel.SelectWarp(hit);
                _markerDrag = new MarkerDrag(GameDataMarkerKind.Warp, hit, target);
            }

            return;
        }

        if (spawnTool)
        {
            _viewModel.AddSpawnAt(target.X, target.Y);
        }
        else
        {
            _viewModel.AddWarpAt(target.X, target.Y);
        }
    }

    private void CommitMarkerDrag()
    {
        if (_markerDrag is not { } drag)
        {
            return;
        }

        _markerDrag = null;
        if (drag.Kind == GameDataMarkerKind.Spawn)
        {
            _viewModel.MoveSpawnTo(drag.Index, drag.Current.X, drag.Current.Y);
        }
        else
        {
            _viewModel.MoveWarpSourceTo(drag.Index, drag.Current.X, drag.Current.Y);
        }

        Invalidate();
    }

    private void CancelMarkerDrag()
    {
        if (_markerDrag is null)
        {
            return;
        }

        _markerDrag = null;
        Invalidate();
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

    private void OnCanvasInvalidated() => Invalidate();

    private void OnGameDataChanged()
    {
        if (_markerDrag is { } drag)
        {
            DocumentGameDataState? state = _viewModel.GameData;
            bool selected = drag.Kind == GameDataMarkerKind.Spawn
                ? state?.SelectedSpawn is not null
                : state?.SelectedWarp is not null;
            if (!selected)
            {
                CancelMarkerDrag();
            }
        }

        Invalidate();
    }

    private MapRenderRequest BuildRenderRequest()
    {
        MapEditSession session = _viewModel.Session;
        byte mask = _viewModel.LayerVisibility;

        MapTileCoordinate? hovered = _viewModel.HoverX is { } hoverX && _viewModel.HoverY is { } hoverY
            ? new MapTileCoordinate(hoverX, hoverY)
            : null;
        MapTileCoordinate? selected = _viewModel.SelectedX is { } selectedX && _viewModel.SelectedY is { } selectedY
            ? new MapTileCoordinate(selectedX, selectedY)
            : null;
        BlockPreview? blockPreview = _rectDrag is { } drag && drag.Purpose is not RectDragPurpose.Select
            ? new BlockPreview(drag.Current, drag.Purpose == RectDragPurpose.Block)
            : null;
        MapRenderOptions options = new(
            new MapLayerVisibility(mask),
            _viewModel.ShowGrid,
            _viewModel.ShowBlocked,
            hovered,
            selected,
            SelectionRectangle: _viewModel.SelectionRectangle,
            PasteGhost: _viewModel.PasteGhost,
            BlockPreview: blockPreview,
            SpawnMarkers: BuildSpawnMarkers(),
            WarpMarkers: BuildWarpMarkers(),
            PreviewMode: _viewModel.GameData?.PreviewMode ?? false,
            NpcPreviews: BuildNpcPreviews());
        return new MapRenderRequest(session.Document, _viewport, options);
    }

    private IReadOnlyList<GameDataMarkerInput>? BuildSpawnMarkers()
    {
        DocumentGameDataState? state = _viewModel.GameData;
        if (state is null || !state.ShowSpawnOverlay || state.Session is not { Edits: { } edits })
        {
            return null;
        }

        var spawns = edits.Spawns;
        var markers = new GameDataMarkerInput[spawns.Count];
        for (int i = 0; i < spawns.Count; i++)
        {
            NpcSpawnRow spawn = spawns[i];
            MapTileCoordinate tile = new(spawn.MapX, spawn.MapY);
            if (_markerDrag is { } drag && drag.Kind == GameDataMarkerKind.Spawn && drag.Index == i)
            {
                tile = drag.Current;
            }

            markers[i] = new GameDataMarkerInput(i, tile, state.SelectedSpawn == i, $"npc {spawn.NpcId}");
        }

        return markers;
    }

    private IReadOnlyList<GameDataMarkerInput>? BuildWarpMarkers()
    {
        DocumentGameDataState? state = _viewModel.GameData;
        if (state is null || !state.ShowWarpOverlay || state.Session is not { Edits: { } edits })
        {
            return null;
        }

        var warps = edits.Warps;
        var markers = new GameDataMarkerInput[warps.Count];
        for (int i = 0; i < warps.Count; i++)
        {
            WarpRow warp = warps[i];
            MapTileCoordinate tile = new(warp.MapX, warp.MapY);
            if (_markerDrag is { } drag && drag.Kind == GameDataMarkerKind.Warp && drag.Index == i)
            {
                tile = drag.Current;
            }

            markers[i] = new GameDataMarkerInput(i, tile, state.SelectedWarp == i, $"map {warp.WarpId} ({warp.WarpX}, {warp.WarpY})");
        }

        return markers;
    }

    private IReadOnlyList<NpcAppearanceGroup>? BuildNpcPreviews()
    {
        DocumentGameDataState? state = _viewModel.GameData;
        if (state is null || !state.PreviewMode || !state.ShowSpawnOverlay || state.Session is not { Edits: { } edits })
        {
            return null;
        }

        if (_assets.Current.Appearance is not { } catalog)
        {
            return null;
        }

        NpcAppearanceComposer composer = new(catalog);
        IReadOnlyDictionary<int, NpcAppearance> npcs = state.Session.Npcs;
        var spawns = edits.Spawns;
        var groups = new NpcAppearanceGroup[spawns.Count];
        for (int i = 0; i < spawns.Count; i++)
        {
            NpcSpawnRow spawn = spawns[i];
            MapTileCoordinate tile = new(spawn.MapX, spawn.MapY);
            if (_markerDrag is { } drag && drag.Kind == GameDataMarkerKind.Spawn && drag.Index == i)
            {
                tile = drag.Current;
            }

            NpcAppearance? appearance = npcs.TryGetValue(spawn.NpcId, out NpcAppearance found) ? found : null;
            groups[i] = composer.Compose(appearance ?? default, i, tile.X, tile.Y);
        }

        return groups;
    }
}
