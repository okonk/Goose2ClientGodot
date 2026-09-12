using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapEditor.App.Rendering;
using MapEditor.App.ViewModels;
using MapEditor.Rendering;

namespace MapEditor.App.Controls;

internal sealed class AnimationPreviewControl : Control
{
    private static readonly Brush DiagnosticFill = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x99, 0x66));

    private readonly GraphicViewerViewModel _viewModel;
    private AssetContext _assets;
    private string? _diagnostic;

    public AnimationPreviewControl(GraphicViewerViewModel viewModel, AssetContext assets)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public string? Diagnostic => _diagnostic;

    internal AssetContext Assets
    {
        get => _assets;
        set
        {
            if (ReferenceEquals(_assets, value))
            {
                return;
            }

            _assets = value ?? throw new ArgumentNullException(nameof(value));
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public event EventHandler? DiagnosticChanged;

    internal void RenderPreview(IMapDrawTarget target)
    {
        SpriteReference? reference = _viewModel.CurrentFrame;
        if (reference is null)
        {
            SetDiagnostic(null);
            return;
        }

        Size area = PreviewSize;
        SpriteResolution resolution = _assets.Resolve(reference.Value);
        if (resolution.Image is AvaloniaSpriteSheetImage image)
        {
            SetDiagnostic(null);
            SpriteSourceRect source = resolution.SourceRect;
            target.DrawImage(
                image.Bitmap,
                new Rect(source.X, source.Y, source.Width, source.Height),
                new Rect((area.Width - source.Width) / 2.0, area.Height - source.Height, source.Width, source.Height));
            return;
        }

        string diagnostic = resolution.Diagnostic
            ?? $"Unable to resolve sheet {reference.Value.Sheet} graphic {reference.Value.Graphic} ({resolution.Status}).";
        SetDiagnostic(diagnostic);
        target.DrawText(diagnostic, new Point(area.Width / 2.0, area.Height / 2.0), 12.0, DiagnosticFill, null);
    }

    protected override Size MeasureOverride(Size availableSize) => PreviewSize;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RenderPreview(new DrawingContextMapDrawTarget(context));
    }

    private Size PreviewSize
    {
        get
        {
            SpriteManifest? manifest = _assets.Cache.Manifest;
            return manifest is null
                ? new Size(0, 0)
                : new Size(manifest.MaxFrameWidth, manifest.MaxFrameHeight);
        }
    }

    private void SetDiagnostic(string? diagnostic)
    {
        if (_diagnostic == diagnostic)
        {
            return;
        }

        _diagnostic = diagnostic;
        DiagnosticChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GraphicViewerViewModel.CurrentFrame))
        {
            InvalidateVisual();
        }
    }
}
