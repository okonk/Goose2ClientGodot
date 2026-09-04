using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Controls;

internal enum RectDragPurpose
{
    Select,
    Block,
    Unblock
}

internal readonly record struct RectDrag(
    RectDragPurpose Purpose,
    MapTileCoordinate Origin,
    MapTileRectangle Current);
