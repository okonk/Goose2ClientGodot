using System;
using System.Globalization;
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

    public void DrawText(string text, Point center, double fontSize, Brush fill, Brush? stroke)
    {
        FormattedText main = CreateFormattedText(text, fontSize, fill);
        Point origin = new(center.X - main.Width / 2, center.Y - main.Height / 2);
        if (stroke is not null)
        {
            FormattedText outline = CreateFormattedText(text, fontSize, stroke);
            double offset = Math.Max(fontSize / 16.0, 1.0);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    _context.DrawText(outline, new Point(origin.X + dx * offset, origin.Y + dy * offset));
                }
            }
        }

        _context.DrawText(main, origin);
    }

    private static FormattedText CreateFormattedText(string text, double fontSize, Brush brush)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), fontSize, brush);

    public IDisposable PushClip(Rect rect)
        => _context.PushClip(rect);
}
