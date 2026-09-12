using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using MapEditor.App.Rendering;
using MapEditor.App.ViewModels;
using MapEditor.Rendering;

namespace MapEditor.App.Controls;

internal sealed class GraphicSheetControl : Control, ICustomHitTest
{
    private const double WheelZoomStep = 1.25;

    private static readonly Brush TransparentFill = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    private static readonly Pen FrameOutline = new(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), 1.0);
    private static readonly Pen SelectionOutline = new(new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)), 2.0);

    private readonly GraphicViewerViewModel _viewModel;
    private AvaloniaSpriteSheetImage? _image;

    public GraphicSheetControl(GraphicViewerViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public AvaloniaSpriteSheetImage? Image
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value))
            {
                return;
            }

            _image = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public double Zoom
    {
        get => _viewModel.Zoom;
        set => _viewModel.Zoom = value;
    }

    internal void RenderSheet(IMapDrawTarget target)
    {
        AvaloniaSpriteSheetImage? image = _image;
        if (image is null)
        {
            return;
        }

        double zoom = _viewModel.Zoom;
        target.DrawImage(
            image.Bitmap,
            new Rect(0, 0, image.PixelWidth, image.PixelHeight),
            new Rect(0, 0, image.PixelWidth * zoom, image.PixelHeight * zoom));

        foreach (SpriteFrame frame in _viewModel.Frames)
        {
            target.DrawRectangle(TransparentFill, FrameOutline, Scaled(frame.SourceRect, zoom));
        }

        if (_viewModel.SelectedFrame is { } selected)
        {
            target.DrawRectangle(TransparentFill, SelectionOutline, Scaled(selected.SourceRect, zoom));
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        AvaloniaSpriteSheetImage? image = _image;
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_viewModel.TrySelectFrameAt(point.Position.X, point.Position.Y))
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0)
        {
            return;
        }

        Zoom *= e.Delta.Y > 0 ? WheelZoomStep : 1.0 / WheelZoomStep;
        e.Handled = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GraphicViewerViewModel.Frames):
            case nameof(GraphicViewerViewModel.SelectedFrame):
                InvalidateVisual();
                break;
            case nameof(GraphicViewerViewModel.Zoom):
                InvalidateMeasure();
                InvalidateVisual();
                break;
        }
    }

    private static Rect Scaled(SpriteSourceRect rect, double zoom)
        => new(rect.X * zoom, rect.Y * zoom, rect.Width * zoom, rect.Height * zoom);
}
