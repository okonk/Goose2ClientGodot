using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using MapEditor.App.Rendering;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Controls;

internal sealed class SpritePaletteControl : Control, ICustomHitTest
{
    internal const double CellSize = 36;
    private const double ThumbnailSize = 32;
    private const double WheelStep = CellSize * 3;

    private static readonly Brush PlaceholderFill = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0xCC));
    private static readonly Pen PlaceholderStroke = new(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)), 1.0);
    private static readonly Brush TransparentFill = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    private static readonly Pen SelectionStroke = new(new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)), 2.0);

    private readonly MapDocumentViewModel _viewModel;
    private readonly AssetContextController _assets;
    private readonly Func<AssetContext, SpriteReference, SpriteResolution> _resolve;
    private ScrollBar? _bar;
    private double _offset;

    public SpritePaletteControl(MapDocumentViewModel viewModel, AssetContextController assets)
        : this(viewModel, assets, (context, reference) => context.Resolve(reference))
    {
    }

    internal SpritePaletteControl(
        MapDocumentViewModel viewModel,
        AssetContextController assets,
        Func<AssetContext, SpriteReference, SpriteResolution> resolve)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        _viewModel.PaletteInvalidated += OnPaletteInvalidated;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged += OnSizeChanged;
    }

    public double Offset
    {
        get => _offset;
        set
        {
            double clamped = ClampOffset(value);
            if (clamped != _offset)
            {
                _offset = clamped;
                SyncBar();
                InvalidateVisual();
            }
        }
    }

    public int Columns => Math.Max(1, (int)(Bounds.Width / CellSize));

    public int FrameCount => Frames.Count;

    public double ExtentHeight => Math.Ceiling(FrameCount / (double)Columns) * CellSize;

    public double ViewportHeight => Bounds.Height;

    public void BindScrollBar(ScrollBar bar)
    {
        if (bar is null)
        {
            throw new ArgumentNullException(nameof(bar));
        }

        if (ReferenceEquals(_bar, bar))
        {
            return;
        }

        if (_bar is { } previous)
        {
            previous.ValueChanged -= OnBarValueChanged;
        }

        _bar = bar;
        bar.ValueChanged += OnBarValueChanged;
        SyncBar();
    }

    internal void RenderPalette(IMapDrawTarget target)
    {
        using IDisposable clip = target.PushClip(new Rect(Bounds.Size));
        IReadOnlyList<SpriteFrame> frames = Frames;
        int columns = Columns;
        int rowCount = (frames.Count + columns - 1) / columns;
        if (rowCount == 0)
        {
            return;
        }

        int firstRow = (int)Math.Clamp(Math.Floor(_offset / CellSize), 0, rowCount - 1);
        int lastRow = (int)Math.Clamp(Math.Ceiling((_offset + ViewportHeight) / CellSize) - 1, 0, rowCount - 1);
        for (int row = firstRow; row <= lastRow; row++)
        {
            double y = row * CellSize - _offset;
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                if (index >= frames.Count)
                {
                    break;
                }

                DrawFrame(target, frames[index], column * CellSize, y);
            }
        }

        MapTileLayer brush = _viewModel.Brush;
        if (brush.Sheet != _viewModel.SelectedSheet)
        {
            return;
        }

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                if (index >= frames.Count)
                {
                    break;
                }

                if (frames[index].Reference.Graphic != brush.Graphic)
                {
                    continue;
                }

                double x = column * CellSize;
                double y = row * CellSize - _offset;
                target.DrawRectangle(TransparentFill, SelectionStroke, new Rect(x + 1, y + 1, CellSize - 2, CellSize - 2));
                return;
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RenderPalette(new DrawingContextMapDrawTarget(context));
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

        if (FrameAt(point.Position) is { } frame)
        {
            _viewModel.Brush = new MapTileLayer(_viewModel.SelectedSheet, frame.Reference.Graphic);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0)
        {
            return;
        }

        Offset -= e.Delta.Y * WheelStep;
        e.Handled = true;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _offset = ClampOffset(_offset);
        SyncBar();
        InvalidateVisual();
    }

    private void OnPaletteInvalidated()
    {
        _offset = ClampOffset(_offset);
        SyncBar();
        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MapDocumentViewModel.SelectedSheet):
                OnPaletteInvalidated();
                break;
            case nameof(MapDocumentViewModel.Brush):
                InvalidateVisual();
                break;
        }
    }

    private void OnBarValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!ReferenceEquals(_bar, sender) || e.NewValue == _offset)
        {
            return;
        }

        Offset = e.NewValue;
    }

    private void SyncBar()
    {
        if (_bar is not { } bar)
        {
            return;
        }

        bar.ViewportSize = ViewportHeight;
        bar.Maximum = Math.Max(0, ExtentHeight - ViewportHeight);
        bar.IsVisible = bar.Maximum > 0;
        if (bar.Value != _offset)
        {
            bar.Value = _offset;
        }
    }

    private double ClampOffset(double value)
    {
        double maximum = Math.Max(0, ExtentHeight - ViewportHeight);
        return Math.Clamp(value, 0, maximum);
    }

    private IReadOnlyList<SpriteFrame> Frames => _assets.Current.GetFrames(_viewModel.SelectedSheet);

    private SpriteFrame? FrameAt(Point position)
    {
        IReadOnlyList<SpriteFrame> frames = Frames;
        int columns = Columns;
        int row = (int)Math.Floor((position.Y + _offset) / CellSize);
        int column = (int)Math.Floor(position.X / CellSize);
        if (row < 0 || column < 0 || column >= columns)
        {
            return null;
        }

        int index = row * columns + column;
        return index < frames.Count ? frames[index] : null;
    }

    private void DrawFrame(IMapDrawTarget target, SpriteFrame frame, double x, double y)
    {
        double padding = (CellSize - ThumbnailSize) / 2;
        Rect box = new(x + padding, y + padding, ThumbnailSize, ThumbnailSize);
        SpriteResolution resolution = _resolve(_assets.Current, frame.Reference);
        if (resolution.Image is AvaloniaSpriteSheetImage image)
        {
            target.DrawImage(
                image.Bitmap,
                new Rect(resolution.SourceRect.X, resolution.SourceRect.Y, resolution.SourceRect.Width, resolution.SourceRect.Height),
                box);
            return;
        }

        target.DrawRectangle(PlaceholderFill, PlaceholderStroke, box);
        target.DrawLine(PlaceholderStroke, new Point(box.Left, box.Top), new Point(box.Right, box.Bottom));
        target.DrawLine(PlaceholderStroke, new Point(box.Right, box.Top), new Point(box.Left, box.Bottom));
    }
}
