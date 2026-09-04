using System;
using MapEditor.Core;

namespace MapEditor.App;

public enum LayerClickMode
{
    Plain,
    Toggle,
    Range,
}

internal static class LayerSelection
{
    public static (byte Next, int Anchor) Apply(byte current, int anchor, int layer, LayerClickMode mode)
    {
        byte next = mode switch
        {
            LayerClickMode.Plain => Plain(layer),
            LayerClickMode.Toggle => Toggle(current, layer, keepNonEmpty: true),
            _ => Range(anchor, layer),
        };

        int nextAnchor = mode == LayerClickMode.Range ? anchor : layer;
        return (next, nextAnchor);
    }

    public static byte Plain(int layer)
    {
        Validate(layer);
        return (byte)(1 << layer);
    }

    public static byte Toggle(byte current, int layer, bool keepNonEmpty = false)
    {
        Validate(layer);
        byte next = (byte)(current ^ (1 << layer));
        return keepNonEmpty && next == 0 ? (byte)(1 << layer) : next;
    }

    public static byte Range(int anchor, int layer)
    {
        Validate(anchor);
        Validate(layer);
        int lo = Math.Min(anchor, layer);
        int hi = Math.Max(anchor, layer);
        byte next = 0;
        for (int l = lo; l <= hi; l++)
        {
            next |= (byte)(1 << l);
        }

        return next;
    }

    private static void Validate(int layer)
    {
        if (layer < 0 || layer >= MapDocument.LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(layer));
        }
    }
}
