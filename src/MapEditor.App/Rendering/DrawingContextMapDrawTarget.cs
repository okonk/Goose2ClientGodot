using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MapEditor.App.Rendering;

internal sealed class DrawingContextMapDrawTarget : IMapDrawTarget
{
    private readonly DrawingContext _context;

    public DrawingContextMapDrawTarget(DrawingContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public void DrawImage(Bitmap bitmap, Rect sourceRect, Rect destinationRect)
        => _context.DrawImage(bitmap, sourceRect, destinationRect);

    public void DrawLine(Pen pen, Point start, Point end)
        => _context.DrawLine(pen, start, end);

    public void DrawRectangle(Brush fill, Pen? stroke, Rect rect)
        => _context.DrawRectangle(fill, stroke, rect);

    public IDisposable PushClip(Rect rect)
        => _context.PushClip(rect);
}
