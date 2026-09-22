using System;
using Godot;
using MapEditor.Core;

namespace Goose2Client.Map;

public static class MinimapBitmapBuilder
{
    public static Image Build(MapDocument map, Func<int, int, Color?> provider)
    {
        var image = Image.Create(map.Width, map.Height, false, Image.Format.Rgba8);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                image.SetPixelv(new Vector2I(x, y), MinimapColors.PickColor(map[x, y], provider) ?? MinimapColors.Empty);
        return image;
    }
}
