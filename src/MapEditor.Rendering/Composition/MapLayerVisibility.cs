using System;
using MapEditor.Core;

namespace MapEditor.Rendering;

public readonly record struct MapLayerVisibility
{
    private const int LayerCount = MapDocument.LayerCount;
    private const byte FullMask = 0b11111;

    public static MapLayerVisibility All { get; } = new(FullMask);

    public static MapLayerVisibility None { get; } = new(0);

    public byte Mask { get; }

    public MapLayerVisibility(byte mask)
    {
        if (mask > FullMask)
        {
            throw new ArgumentOutOfRangeException(nameof(mask));
        }

        Mask = mask;
    }

    public bool IsVisible(int layerIndex)
    {
        CheckLayerIndex(layerIndex);
        return (Mask & (1 << layerIndex)) != 0;
    }

    public MapLayerVisibility WithVisibility(int layerIndex, bool visible)
    {
        CheckLayerIndex(layerIndex);
        byte mask = visible ? (byte)(Mask | (1 << layerIndex)) : (byte)(Mask & ~(1 << layerIndex));
        return new MapLayerVisibility(mask);
    }

    private static void CheckLayerIndex(int layerIndex)
    {
        if (layerIndex < 0 || layerIndex >= LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        }
    }
}
