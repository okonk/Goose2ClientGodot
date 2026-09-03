using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MapEditor.App.Rendering;

internal interface IMapDrawTarget
{
    void DrawImage(Bitmap bitmap, Rect sourceRect, Rect destinationRect);

    void DrawLine(Pen pen, Point start, Point end);

    void DrawRectangle(Brush fill, Pen? stroke, Rect rect);

    IDisposable PushClip(Rect rect);
}
