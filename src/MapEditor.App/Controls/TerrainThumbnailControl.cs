using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapEditor.App.Rendering;
using MapEditor.App.ViewModels;
using MapEditor.Rendering;

namespace MapEditor.App.Controls;

internal sealed class TerrainThumbnailControl : Control
{
    private const double ThumbnailSize = 32;

    private static readonly Brush PlaceholderFill = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0xCC));
    private static readonly Pen PlaceholderStroke = new(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)), 1.0);

    private TerrainChoice? _choice;
    private AssetContextController? _assets;
    private bool _attached;

    public static readonly DirectProperty<TerrainThumbnailControl, TerrainChoice?> ChoiceProperty =
        AvaloniaProperty.RegisterDirect<TerrainThumbnailControl, TerrainChoice?>(nameof(Choice), o => o.Choice, (o, v) => o.Choice = v);

    public static readonly DirectProperty<TerrainThumbnailControl, AssetContextController?> AssetsProperty =
        AvaloniaProperty.RegisterDirect<TerrainThumbnailControl, AssetContextController?>(nameof(Assets), o => o.Assets, (o, v) => o.Assets = v);

    public TerrainThumbnailControl()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        MinWidth = MinHeight = MaxWidth = MaxHeight = ThumbnailSize + 8;
    }

    public TerrainThumbnailControl(TerrainChoice choice)
        : this()
    {
        Choice = choice;
    }

    public TerrainChoice? Choice
    {
        get => _choice;
        set
        {
            if (SetAndRaise(ChoiceProperty, ref _choice, value))
            {
                InvalidateVisual();
            }
        }
    }

    public AssetContextController? Assets
    {
        get => _assets;
        set
        {
            AssetContextController? previous = _assets;
            if (ReferenceEquals(previous, value))
            {
                return;
            }

            if (_attached)
            {
                previous?.CurrentChanged -= OnAssetsCurrentChanged;
                value?.CurrentChanged += OnAssetsCurrentChanged;
            }

            SetAndRaise(AssetsProperty, ref _assets, value);
            InvalidateVisual();
        }
    }

    internal void RenderPalette(IMapDrawTarget target)
    {
        using IDisposable clip = target.PushClip(new Rect(Bounds.Size));
        double padding = (Bounds.Width - ThumbnailSize) / 2;
        Rect box = new(padding, padding, ThumbnailSize, ThumbnailSize);
        if (_assets is not { } assets || Choice?.Representative is not { } representative)
        {
            DrawPlaceholder(target, box);
            return;
        }

        SpriteResolution resolution = assets.Current.Resolve(new SpriteReference(representative.Reference.Sheet, representative.Reference.Graphic));
        if (resolution.Image is AvaloniaSpriteSheetImage image)
        {
            target.DrawImage(
                image.Bitmap,
                new Rect(resolution.SourceRect.X, resolution.SourceRect.Y, resolution.SourceRect.Width, resolution.SourceRect.Height),
                box);
            return;
        }

        DrawPlaceholder(target, box);
    }

    private static void DrawPlaceholder(IMapDrawTarget target, Rect box)
    {
        target.DrawRectangle(PlaceholderFill, PlaceholderStroke, box);
        target.DrawLine(PlaceholderStroke, new Point(box.Left, box.Top), new Point(box.Right, box.Bottom));
        target.DrawLine(PlaceholderStroke, new Point(box.Right, box.Top), new Point(box.Left, box.Bottom));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RenderPalette(new DrawingContextMapDrawTarget(context));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_attached)
        {
            return;
        }

        _attached = true;
        _assets?.CurrentChanged += OnAssetsCurrentChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _assets?.CurrentChanged -= OnAssetsCurrentChanged;
    }

    private void OnAssetsCurrentChanged(object? sender, EventArgs e) => InvalidateVisual();
}
