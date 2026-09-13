using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using MapEditor.App.Rendering;
using MapEditor.App.Terrain;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Controls;

internal sealed class TerrainSheetControl : Control, ICustomHitTest, IDisposable
{
    private const double WheelZoomStep = 1.25;
    private const byte OverlayAlpha = 0xCC;

    private static readonly IReadOnlyList<SpriteFrame> NoFrames = Array.Empty<SpriteFrame>();

    private static readonly TerrainPeer[] PeerSlots =
    {
        TerrainPeer.Center, TerrainPeer.North, TerrainPeer.East, TerrainPeer.South, TerrainPeer.West,
        TerrainPeer.NorthEast, TerrainPeer.SouthEast, TerrainPeer.SouthWest, TerrainPeer.NorthWest
    };

    private readonly TerrainEditorViewModel _viewModel;
    private readonly TerrainSheetImageController _images;
    private bool _disposed;

    private bool _painting;
    private IPointer? _paintPointer;
    private double _paintZoom;
    private Size _paintSheetSize;
    private IReadOnlyList<SpriteFrame> _paintFrames = NoFrames;
    private Point _lastSourcePoint;

    public TerrainSheetControl(TerrainEditorViewModel viewModel, string assetDirectory)
        : this(viewModel, assetDirectory, new AvaloniaSpriteSheetLoader())
    {
    }

    internal TerrainSheetControl(TerrainEditorViewModel viewModel, string assetDirectory, ISpriteSheetLoader loader)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _images = new TerrainSheetImageController(assetDirectory ?? throw new ArgumentNullException(nameof(assetDirectory)), loader);
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.CanvasInvalidated += OnCanvasInvalidated;
        _images.ImageChanged += OnImageChanged;
        _images.SelectSheet(_viewModel.SelectedSheet);
    }

    internal TerrainSheetImageController Images => _images;

    public string? Diagnostic => _images.Diagnostic;

    internal bool IsPainting => _painting;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_painting)
        {
            EndPaintGesture(commit: false);
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.CanvasInvalidated -= OnCanvasInvalidated;
        _images.ImageChanged -= OnImageChanged;
        _images.Dispose();
    }

    internal void RenderSheet(IMapDrawTarget target)
    {
        AvaloniaSpriteSheetImage? image = _images.Image as AvaloniaSpriteSheetImage;
        if (image is null)
        {
            return;
        }

        double zoom = _viewModel.Zoom;
        target.DrawImage(
            image.Bitmap,
            new Rect(0, 0, image.PixelWidth, image.PixelHeight),
            new Rect(0, 0, image.PixelWidth * zoom, image.PixelHeight * zoom));

        Dictionary<Guid, TerrainColor> colors = new(_viewModel.CurrentCatalog.Terrains.Count);
        foreach (TerrainDefinition terrain in _viewModel.CurrentCatalog.Terrains)
        {
            colors[terrain.Id] = terrain.DisplayColor;
        }

        Dictionary<TerrainGraphicReference, SpriteFrame> frames = new(_viewModel.EligibleFrames.Count);
        foreach (SpriteFrame frame in _viewModel.EligibleFrames)
        {
            frames[new TerrainGraphicReference(frame.Reference.Sheet, frame.Reference.Graphic)] = frame;
        }

        foreach (TerrainGraphicDefinition graphic in _viewModel.CurrentCatalog.Graphics)
        {
            if (!frames.TryGetValue(graphic.Reference, out SpriteFrame frame))
            {
                continue;
            }

            Point origin = new(frame.SourceRect.X * zoom, frame.SourceRect.Y * zoom);
            foreach (TerrainPeer peer in PeerSlots)
            {
                if (graphic.Pattern.Get(peer) is { } terrainId && colors.TryGetValue(terrainId, out TerrainColor color))
                {
                    var brush = new SolidColorBrush(Color.FromArgb(OverlayAlpha, color.R, color.G, color.B));
                    target.DrawPolygon(brush, null, TerrainRegionGeometry.ToScreenPolygon(peer, origin, zoom));
                }
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ISpriteSheetImage? image = _images.Image;
        if (image is null)
        {
            return new Size(0, 0);
        }

        double zoom = _viewModel.Zoom;
        return new Size(image.PixelWidth * zoom, image.PixelHeight * zoom);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RenderSheet(new DrawingContextMapDrawTarget(context));
    }

    bool ICustomHitTest.HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_painting || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0)
        {
            return;
        }

        _viewModel.Zoom *= e.Delta.Y > 0 ? WheelZoomStep : 1.0 / WheelZoomStep;
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_painting || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_images.Image is not { } image || _viewModel.EligibleFrames.Count == 0)
        {
            return;
        }

        bool clear = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        Guid? value = clear ? null : _viewModel.SelectedTerrain?.Id;
        if (value is null && !clear)
        {
            return;
        }

        double zoom = _viewModel.Zoom;
        Point source = e.GetCurrentPoint(this).Position / zoom;
        IReadOnlyList<SpriteFrame> frames = _viewModel.EligibleFrames;
        if (HitFrame(source, frames) is not { } frame
            || TerrainRegionGeometry.HitTestSource(source - new Point(frame.SourceRect.X, frame.SourceRect.Y)) is not { } peer)
        {
            return;
        }

        _viewModel.BeginRegionStroke(value);
        _painting = true;
        _paintZoom = zoom;
        _paintSheetSize = new Size(image.PixelWidth, image.PixelHeight);
        _paintFrames = frames;
        _lastSourcePoint = source;
        _paintPointer = e.Pointer;
        _viewModel.VisitRegion(RegionKey(frame, peer));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_painting || e.Pointer != _paintPointer)
        {
            return;
        }

        Point position = e.GetCurrentPoint(this).Position;
        if (position.X < 0 || position.Y < 0
            || position.X >= _paintSheetSize.Width * _paintZoom
            || position.Y >= _paintSheetSize.Height * _paintZoom)
        {
            return;
        }

        Point source = position / _paintZoom;
        List<Point> samples = new();
        TerrainSheetLine.AppendSamples(_lastSourcePoint, source, samples);
        foreach (Point sample in samples)
        {
            if (HitFrame(sample, _paintFrames) is { } frame
                && TerrainRegionGeometry.HitTestSource(sample - new Point(frame.SourceRect.X, frame.SourceRect.Y)) is { } peer)
            {
                _viewModel.VisitRegion(RegionKey(frame, peer));
            }
        }

        _lastSourcePoint = source;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_painting || e.Pointer != _paintPointer)
        {
            return;
        }

        EndPaintGesture(commit: true);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_painting)
        {
            return;
        }

        EndPaintGesture(commit: false);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _painting)
        {
            EndPaintGesture(commit: false);
            e.Handled = true;
        }
    }

    // Flags clear before Capture(null): releasing capture can reenter
    // OnPointerCaptureLost synchronously and must not cancel a finished stroke.
    private void EndPaintGesture(bool commit)
    {
        _painting = false;
        if (commit)
        {
            _viewModel.CompleteRegionStroke();
        }
        else
        {
            _viewModel.CancelRegionStroke();
        }

        _paintFrames = NoFrames;
        _lastSourcePoint = default;
        IPointer? pointer = _paintPointer;
        _paintPointer = null;
        pointer?.Capture(null);
    }

    // Matches GraphicViewerViewModel's frame selection: half-open source rect,
    // lowest graphic id wins on overlap.
    private static SpriteFrame? HitFrame(Point source, IReadOnlyList<SpriteFrame> frames)
    {
        SpriteFrame? hit = null;
        foreach (SpriteFrame frame in frames)
        {
            SpriteSourceRect rect = frame.SourceRect;
            if (source.X >= rect.X && source.X < rect.X + rect.Width
                && source.Y >= rect.Y && source.Y < rect.Y + rect.Height)
            {
                if (hit is null || frame.Reference.Graphic < hit.Value.Reference.Graphic)
                {
                    hit = frame;
                }
            }
        }

        return hit;
    }

    private static TerrainRegionKey RegionKey(SpriteFrame frame, TerrainPeer peer)
        => new(new TerrainGraphicReference(frame.Reference.Sheet, frame.Reference.Graphic), peer);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TerrainEditorViewModel.SelectedSheet):
                _images.SelectSheet(_viewModel.SelectedSheet);
                break;
            case nameof(TerrainEditorViewModel.Zoom):
                InvalidateMeasure();
                InvalidateVisual();
                break;
            case nameof(TerrainEditorViewModel.EligibleFrames):
                InvalidateVisual();
                break;
        }
    }

    private void OnCanvasInvalidated() => InvalidateVisual();

    private void OnImageChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }
}
