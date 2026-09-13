using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapEditor.App.Rendering;

namespace MapEditor.App.Tests.Fakes;

public sealed class RecordingMapDrawTarget : IMapDrawTarget
{
    public sealed record ImageDraw(Bitmap Image, Rect Source, Rect Destination);

    public sealed record LineDraw(Pen Pen, Point Start, Point End);

    public sealed record RectangleDraw(Brush Fill, Pen? Stroke, Rect Bounds);

    public sealed record PolygonDraw(Brush Fill, Pen? Stroke, IReadOnlyList<Point> Points);

    public sealed record TextDraw(string Text, Point Center, double FontSize, Brush Fill, Brush? Stroke);

    public List<ImageDraw> Images { get; } = new();

    public List<LineDraw> Lines { get; } = new();

    public List<RectangleDraw> Rectangles { get; } = new();

    public List<PolygonDraw> Polygons { get; } = new();

    public List<TextDraw> Texts { get; } = new();

    public List<Rect> Clips { get; } = new();

    public void DrawImage(Bitmap bitmap, Rect sourceRect, Rect destinationRect)
        => Images.Add(new ImageDraw(bitmap, sourceRect, destinationRect));

    public void DrawLine(Pen pen, Point start, Point end)
        => Lines.Add(new LineDraw(pen, start, end));

    public void DrawRectangle(Brush fill, Pen? stroke, Rect rect)
        => Rectangles.Add(new RectangleDraw(fill, stroke, rect));

    public void DrawPolygon(Brush fill, Pen? stroke, IReadOnlyList<Point> points)
        => Polygons.Add(new PolygonDraw(fill, stroke, points.ToArray()));

    public void DrawText(string text, Point center, double fontSize, Brush fill, Brush? stroke)
        => Texts.Add(new TextDraw(text, center, fontSize, fill, stroke));

    public IDisposable PushClip(Rect rect)
    {
        Clips.Add(rect);
        return new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
