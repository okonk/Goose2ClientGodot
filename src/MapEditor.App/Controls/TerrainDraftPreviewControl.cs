using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using MapEditor.App.Rendering;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using MapEditor.Rendering;

namespace MapEditor.App.Controls;

internal sealed class TerrainDraftPreviewControl : Control
{
    internal const double CellSize = 40;
    private static readonly Brush PlaceholderFill = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0xCC));
    private static readonly Pen PlaceholderStroke = new(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)), 1);
    private AssetContext? _context;
    private Func<AssetContext, SpriteReference, SpriteResolution> _resolve = (context, reference) => context.Resolve(reference);
    private IReadOnlyList<TerrainGraphicReference> _variants = Array.Empty<TerrainGraphicReference>();

    internal string? Diagnostic { get; private set; }
    internal event Action<string?>? DiagnosticChanged;

    internal void Configure(
        AssetContext context,
        Func<AssetContext, SpriteReference, SpriteResolution>? resolve = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        if (resolve is not null)
            _resolve = resolve;
        InvalidateVisual();
    }

    internal void SetVariants(IEnumerable<TerrainGraphicReference> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        _variants = Array.AsReadOnly(variants.ToArray());
        InvalidateVisual();
    }

    internal void RenderPreview(IMapDrawTarget target)
    {
        string? previousDiagnostic = Diagnostic;
        Diagnostic = null;
        if (_context is null)
            return;
        using IDisposable clip = target.PushClip(new Rect(Bounds.Size));
        int visible = Math.Min(_variants.Count, Math.Max(0, (int)Math.Ceiling(Bounds.Width / CellSize)));
        for (var i = 0; i < visible; i++)
        {
            TerrainGraphicReference variant = _variants[i];
            SpriteResolution resolution = _resolve(_context, new SpriteReference(variant.Sheet, variant.Graphic));
            Rect destination = new(i * CellSize + 4, 4, CellSize - 8, CellSize - 8);
            if (resolution.Image is AvaloniaSpriteSheetImage image)
            {
                target.DrawImage(image.Bitmap,
                    new Rect(resolution.SourceRect.X, resolution.SourceRect.Y, resolution.SourceRect.Width, resolution.SourceRect.Height),
                    destination);
            }
            else
            {
                Diagnostic ??= resolution.Diagnostic ?? "Preview unavailable.";
                target.DrawRectangle(PlaceholderFill, PlaceholderStroke, destination);
                target.DrawLine(PlaceholderStroke, destination.TopLeft, destination.BottomRight);
                target.DrawLine(PlaceholderStroke, destination.TopRight, destination.BottomLeft);
            }
        }
        if (!string.Equals(previousDiagnostic, Diagnostic, StringComparison.Ordinal))
            DiagnosticChanged?.Invoke(Diagnostic);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RenderPreview(new DrawingContextMapDrawTarget(context));
    }
}
