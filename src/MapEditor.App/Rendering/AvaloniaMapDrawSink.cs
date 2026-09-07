using System;
using Avalonia;
using Avalonia.Media;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class AvaloniaMapDrawSink : IMapDrawSink
{
    private const double StrokeWidth = 1.0;

    internal static readonly Color SpawnMarkerFill = Color.FromArgb(0x80, 0xFF, 0xA0, 0x40);
    internal static readonly Color SpawnMarkerStroke = Color.FromArgb(0xFF, 0xFF, 0xA0, 0x40);
    internal static readonly Color WarpMarkerFill = Color.FromArgb(0x80, 0x40, 0xC0, 0xFF);
    internal static readonly Color WarpMarkerStroke = Color.FromArgb(0xFF, 0x40, 0xC0, 0xFF);
    internal static readonly Color SelectedMarkerStroke = Colors.White;

    private readonly IMapDrawTarget _target;

    public AvaloniaMapDrawSink(IMapDrawTarget target)
        => _target = target ?? throw new ArgumentNullException(nameof(target));

    public void DrawSprite(in SpriteDrawOperation operation)
    {
        if (operation.Image is not AvaloniaSpriteSheetImage image)
        {
            throw new ArgumentException("Sprite image must be an AvaloniaSpriteSheetImage.", nameof(operation));
        }

        _target.DrawImage(
            image.Bitmap,
            new Rect(operation.SourceRect.X, operation.SourceRect.Y, operation.SourceRect.Width, operation.SourceRect.Height),
            new Rect(operation.DestinationRect.X, operation.DestinationRect.Y, operation.DestinationRect.Width, operation.DestinationRect.Height));
    }

    public void DrawPlaceholder(in PlaceholderDrawOperation operation)
    {
        Rect rect = ToRect(operation.DestinationRect);
        Pen stroke = ToPen(operation.StrokeColor);
        _target.DrawRectangle(ToBrush(operation.FillColor), stroke, rect);
        _target.DrawLine(stroke, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Bottom));
        _target.DrawLine(stroke, new Point(rect.Right, rect.Top), new Point(rect.Left, rect.Bottom));
    }

    public void DrawCellOverlay(in CellOverlayDrawOperation operation)
    {
        _target.DrawRectangle(ToBrush(operation.FillColor), ToPen(operation.StrokeColor), ToRect(operation.DestinationRect));
    }

    public void DrawGridLine(in GridLineDrawOperation operation)
    {
        _target.DrawLine(
            ToPen(operation.Color),
            new Point(operation.Start.X, operation.Start.Y),
            new Point(operation.End.X, operation.End.Y));
    }

    public void DrawGameDataMarker(in GameDataMarkerDrawOperation operation)
    {
        Color fill = operation.Kind == GameDataMarkerKind.Spawn ? SpawnMarkerFill : WarpMarkerFill;
        Color stroke = operation.Selected
            ? SelectedMarkerStroke
            : operation.Kind == GameDataMarkerKind.Spawn ? SpawnMarkerStroke : WarpMarkerStroke;
        _target.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(stroke), StrokeWidth), ToRect(operation.DestinationRect));
    }

    private static Rect ToRect(RenderRect rect)
        => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Brush ToBrush(RenderColor color)
        => new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));

    private static Pen ToPen(RenderColor color)
        => new(ToBrush(color), StrokeWidth);
}
