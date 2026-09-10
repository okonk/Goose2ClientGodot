using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using MapEditor.App.Rendering;
using MapEditor.App.ViewModels;
using MapEditor.Core.Terrain;
using MapEditor.Rendering;

namespace MapEditor.App.Controls;

internal sealed class TerrainPaletteControl : Control, ICustomHitTest, IDisposable
{
    internal const double CellHeight = 58;
    private const double ThumbnailSize = 40;
    private const double WheelStep = CellHeight * 3;

    private static readonly Brush PlaceholderFill = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0xCC));
    private static readonly Pen PlaceholderStroke = new(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)), 1);
    private static readonly Brush TextFill = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly Brush WarningFill = new SolidColorBrush(Color.FromRgb(0xE0, 0x80, 0x50));
    private static readonly Brush TransparentFill = new SolidColorBrush(Colors.Transparent);
    private static readonly Pen SelectionStroke = new(new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF)), 2);

    private readonly MapDocumentViewModel _viewModel;
    private readonly AssetContextController _assets;
    private readonly Func<AssetContext, SpriteReference, SpriteResolution> _resolve;
    private ScrollBar? _bar;
    private double _offset;
    private bool _disposed;

    public TerrainPaletteControl(MapDocumentViewModel viewModel, AssetContextController assets)
        : this(viewModel, assets, (context, reference) => context.Resolve(reference))
    {
    }

    internal TerrainPaletteControl(
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

    public int EntryCount => Entries.Count;
    public double ExtentHeight => EntryCount * CellHeight;
    public double ViewportHeight => Bounds.Height;

    public void BindScrollBar(ScrollBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (ReferenceEquals(_bar, bar))
        {
            return;
        }

        UnbindScrollBar();
        _bar = bar;
        bar.ValueChanged += OnBarValueChanged;
        SyncBar();
    }

    internal void UnbindScrollBar()
    {
        if (_bar is { } bar)
        {
            bar.ValueChanged -= OnBarValueChanged;
            _bar = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnbindScrollBar();
        _viewModel.PaletteInvalidated -= OnPaletteInvalidated;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        SizeChanged -= OnSizeChanged;
    }

    internal void RenderPalette(IMapDrawTarget target)
    {
        using IDisposable clip = target.PushClip(new Rect(Bounds.Size));
        IReadOnlyList<TerrainSetDefinition> entries = Entries;
        if (entries.Count == 0)
        {
            return;
        }

        int first = Math.Clamp((int)Math.Floor(_offset / CellHeight), 0, entries.Count - 1);
        int last = Math.Clamp((int)Math.Ceiling((_offset + ViewportHeight) / CellHeight) - 1, 0, entries.Count - 1);
        for (int index = first; index <= last; index++)
        {
            DrawEntry(target, entries[index], index * CellHeight - _offset);
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
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        int index = (int)Math.Floor((e.GetPosition(this).Y + _offset) / CellHeight);
        if (index >= 0 && index < Entries.Count)
        {
            _viewModel.SelectedTerrainId = Entries[index].Id;
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y != 0)
        {
            Offset -= e.Delta.Y * WheelStep;
            e.Handled = true;
        }
    }

    private IReadOnlyList<TerrainSetDefinition> Entries
        => _assets.Current.Terrain.Runtime?.EnabledSets ?? Array.Empty<TerrainSetDefinition>();

    private void DrawEntry(IMapDrawTarget target, TerrainSetDefinition entry, double y)
    {
        Rect row = new(0, y, Bounds.Width, CellHeight);
        if (string.Equals(entry.Id, _viewModel.SelectedTerrainId, StringComparison.Ordinal))
        {
            target.DrawRectangle(TransparentFill, SelectionStroke, row.Deflate(1));
        }

        Rect box = new(8, y + (CellHeight - ThumbnailSize) / 2, ThumbnailSize, ThumbnailSize);
        TerrainGraphicReference representative = entry.Masks.First(mask => mask.Mask == 0).Variants[0];
        SpriteResolution resolution = _resolve(_assets.Current, new SpriteReference(representative.Sheet, representative.Graphic));
        if (resolution.Image is AvaloniaSpriteSheetImage image)
        {
            target.DrawImage(image.Bitmap,
                new Rect(resolution.SourceRect.X, resolution.SourceRect.Y, resolution.SourceRect.Width, resolution.SourceRect.Height), box);
        }
        else
        {
            target.DrawRectangle(PlaceholderFill, PlaceholderStroke, box);
            target.DrawLine(PlaceholderStroke, box.TopLeft, box.BottomRight);
            target.DrawLine(PlaceholderStroke, box.TopRight, box.BottomLeft);
        }

        target.DrawText(entry.DisplayName, new Point(58 + Math.Max(0, Bounds.Width - 58) / 2, y + 20), 13, TextFill, null);
        string topology = entry.Topology == TerrainTopology.FourWay ? "4-way" : "8-way";
        string detail = resolution.Status == SpriteResolutionStatus.Ready
            ? topology
            : $"{topology} · {resolution.Diagnostic ?? "Preview unavailable"}";
        target.DrawText(detail, new Point(58 + Math.Max(0, Bounds.Width - 58) / 2, y + 40), 10,
            resolution.Status == SpriteResolutionStatus.Ready ? TextFill : WarningFill, null);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => RefreshLayout();
    private void OnPaletteInvalidated() => RefreshLayout();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapDocumentViewModel.SelectedTerrainId))
        {
            InvalidateVisual();
        }
    }

    private void RefreshLayout()
    {
        _offset = ClampOffset(_offset);
        SyncBar();
        InvalidateVisual();
    }

    private void OnBarValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _bar))
        {
            Offset = e.NewValue;
        }
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

    private double ClampOffset(double value) => Math.Clamp(value, 0, Math.Max(0, ExtentHeight - ViewportHeight));
}
