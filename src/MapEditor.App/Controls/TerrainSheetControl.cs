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

    private static readonly TerrainPeer[] PeerSlots =
    {
        TerrainPeer.Center, TerrainPeer.North, TerrainPeer.East, TerrainPeer.South, TerrainPeer.West,
        TerrainPeer.NorthEast, TerrainPeer.SouthEast, TerrainPeer.SouthWest, TerrainPeer.NorthWest
    };

    private readonly TerrainEditorViewModel _viewModel;
    private readonly TerrainSheetImageController _images;
    private bool _disposed;

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0)
        {
            return;
        }

        _viewModel.Zoom *= e.Delta.Y > 0 ? WheelZoomStep : 1.0 / WheelZoomStep;
        e.Handled = true;
    }

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
