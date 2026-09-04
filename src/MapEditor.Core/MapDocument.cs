using System;

namespace MapEditor.Core;

public readonly record struct MapTileLayer(int Sheet, int Graphic);

public readonly struct MapTile
{
    private readonly int _flags;
    private readonly MapTileLayer _layer0;
    private readonly MapTileLayer _layer1;
    private readonly MapTileLayer _layer2;
    private readonly MapTileLayer _layer3;
    private readonly MapTileLayer _layer4;

    internal MapTile(int flags, MapTileLayer layer0, MapTileLayer layer1, MapTileLayer layer2, MapTileLayer layer3, MapTileLayer layer4)
    {
        _flags = flags;
        _layer0 = layer0;
        _layer1 = layer1;
        _layer2 = layer2;
        _layer3 = layer3;
        _layer4 = layer4;
    }

    public int Flags => _flags;

    public bool IsBlocked => (Flags & MapDocument.BlockedFlag) != 0;

    public bool IsEmpty => _flags == 0
        && _layer0 == default && _layer1 == default && _layer2 == default
        && _layer3 == default && _layer4 == default;

    public bool IsRoof => GetLayer(MapDocument.LayerCount - 1).Graphic != 0;

    public MapTileLayer GetLayer(int layerIndex)
    {
        return layerIndex switch
        {
            0 => _layer0,
            1 => _layer1,
            2 => _layer2,
            3 => _layer3,
            4 => _layer4,
            _ => throw new ArgumentOutOfRangeException(nameof(layerIndex))
        };
    }

    internal MapTile WithFlags(int flags)
    {
        return new MapTile(flags, _layer0, _layer1, _layer2, _layer3, _layer4);
    }

    internal MapTile WithLayer(int layerIndex, MapTileLayer layer)
    {
        return layerIndex switch
        {
            0 => new MapTile(_flags, layer, _layer1, _layer2, _layer3, _layer4),
            1 => new MapTile(_flags, _layer0, layer, _layer2, _layer3, _layer4),
            2 => new MapTile(_flags, _layer0, _layer1, layer, _layer3, _layer4),
            3 => new MapTile(_flags, _layer0, _layer1, _layer2, layer, _layer4),
            4 => new MapTile(_flags, _layer0, _layer1, _layer2, _layer3, layer),
            _ => throw new ArgumentOutOfRangeException(nameof(layerIndex))
        };
    }
}

public sealed class MapDocument
{
    private MapTile[] _tiles;

    public const int LayerCount = 5;
    public const int BlockedFlag = 2;
    public const int DefaultWidth = 100;
    public const int DefaultHeight = 100;
    public const int MinDimension = 1;
    public const int MaxDimension = 1000;
    public const short NewMapVersion = 1;
    public const short SupportedEditorVersion = 10;
    // Aspereta maps were written by the older editor; the tile layout is identical.
    public const short LegacyEditorVersion = 3;

    public short Version { get; }

    public short EditorVersion { get; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int TileCount => _tiles.Length;

    internal MapDocument(short version, short editorVersion, int width, int height, MapTile[] tiles)
    {
        if (tiles.Length != checked(width * height))
        {
            throw new ArgumentException("Tile array length does not match width * height.", nameof(tiles));
        }

        Version = version;
        EditorVersion = editorVersion;
        Width = width;
        Height = height;
        _tiles = tiles;
    }

    public static MapDocument Create(int width = DefaultWidth, int height = DefaultHeight)
    {
        if (width < MinDimension || width > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height < MinDimension || height > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        return new MapDocument(NewMapVersion, SupportedEditorVersion, width, height, new MapTile[checked(width * height)]);
    }

    internal void ResizeTo(MapTileRectangle window)
    {
        long width = window.Width;
        long height = window.Height;
        long x = window.X;
        long y = window.Y;
        if (width < MinDimension || width > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        if (height < MinDimension || height > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        long minOrigin = -MaxDimension;
        long maxOrigin = MaxDimension;
        if (x < minOrigin || x > maxOrigin)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        if (y < minOrigin || y > maxOrigin)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        int newWidth = (int)width;
        int newHeight = (int)height;
        var newTiles = new MapTile[checked(newWidth * newHeight)];
        int oldStartX = Math.Max(0, (int)x);
        int newStartX = Math.Max(0, -(int)x);
        int copyWidth = (int)Math.Max(0, Math.Min((long)newWidth - newStartX, Width - oldStartX));
        int oldStartY = Math.Max(0, (int)y);
        int newStartY = Math.Max(0, -(int)y);
        int copyHeight = (int)Math.Max(0, Math.Min((long)newHeight - newStartY, Height - oldStartY));
        for (var row = 0; row < copyHeight; row++)
        {
            Array.Copy(_tiles, (row + oldStartY) * Width + oldStartX, newTiles, (row + newStartY) * newWidth + newStartX, copyWidth);
        }

        _tiles = newTiles;
        Width = newWidth;
        Height = newHeight;
    }

    public MapTile this[int x, int y]
    {
        get
        {
            return _tiles[Index(x, y)];
        }
    }

    public MapTile GetTile(int rowMajorIndex)
    {
        if (rowMajorIndex < 0 || rowMajorIndex >= _tiles.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(rowMajorIndex));
        }

        return _tiles[rowMajorIndex];
    }

    public void SetFlags(int x, int y, int flags)
    {
        var index = Index(x, y);
        _tiles[index] = _tiles[index].WithFlags(flags);
    }

    public void SetLayer(int x, int y, int layerIndex, MapTileLayer layer)
    {
        if (layerIndex < 0 || layerIndex >= LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        }

        var index = Index(x, y);
        _tiles[index] = _tiles[index].WithLayer(layerIndex, layer);
    }

    private int Index(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return y * Width + x;
    }
}
