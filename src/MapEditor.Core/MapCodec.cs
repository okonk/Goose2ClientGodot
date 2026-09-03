using System;
using System.Buffers.Binary;

namespace MapEditor.Core;

public static class MapCodec
{
    public const int HeaderSize = 12;
    public const int BytesPerTile = 34;

    // Wire layout, all little-endian: Int16 version, Int16 editor version, Int32 width, Int32 height,
    // then per tile Int32 flags followed by five Int32 graphic, Int16 sheet pairs. Converted Illutia
    // maps append a 404-byte used-sheet index after the tiles; the Godot client ignores it, so does
    // this decoder.
    public static MapDocument Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            throw new MapFormatException(MapFormatError.TruncatedHeader,
                $"Decode: expected at least {HeaderSize} header bytes, got {bytes.Length}.");
        }

        var version = BinaryPrimitives.ReadInt16LittleEndian(bytes);
        var editorVersion = BinaryPrimitives.ReadInt16LittleEndian(bytes[2..]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);

        if (editorVersion != MapDocument.SupportedEditorVersion && editorVersion != MapDocument.LegacyEditorVersion)
        {
            throw new MapFormatException(MapFormatError.UnsupportedEditorVersion,
                $"Decode: unsupported editor version {editorVersion}; expected {MapDocument.SupportedEditorVersion} or {MapDocument.LegacyEditorVersion}.");
        }

        if (width < MapDocument.MinDimension || height < MapDocument.MinDimension)
        {
            throw new MapFormatException(MapFormatError.InvalidDimensions,
                $"Decode: invalid dimensions {width}x{height}.");
        }

        if (width > MapDocument.MaxDimension || height > MapDocument.MaxDimension)
        {
            throw new MapFormatException(MapFormatError.OversizedDimensions,
                $"Decode: dimensions {width}x{height} exceed maximum {MapDocument.MaxDimension}.");
        }

        var tileCount = checked(width * height);
        var expectedLength = checked(HeaderSize + BytesPerTile * tileCount);
        if (bytes.Length < expectedLength)
        {
            throw new MapFormatException(MapFormatError.LengthMismatch,
                $"Decode: expected at least {expectedLength} bytes for a {width}x{height} map, got {bytes.Length}.");
        }

        var tiles = new MapTile[tileCount];
        var offset = HeaderSize;
        for (var i = 0; i < tileCount; i++)
        {
            var flags = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
            offset += 4;
            var layer0 = DecodeLayer(bytes, ref offset);
            var layer1 = DecodeLayer(bytes, ref offset);
            var layer2 = DecodeLayer(bytes, ref offset);
            var layer3 = DecodeLayer(bytes, ref offset);
            var layer4 = DecodeLayer(bytes, ref offset);
            tiles[i] = new MapTile(flags, layer0, layer1, layer2, layer3, layer4);
        }

        return new MapDocument(version, editorVersion, width, height, tiles);
    }

    private static MapTileLayer DecodeLayer(ReadOnlySpan<byte> bytes, ref int offset)
    {
        var graphic = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
        offset += 4;
        var sheet = BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
        offset += 2;
        return new MapTileLayer(sheet, graphic);
    }

    public static byte[] Encode(MapDocument document)
    {
        var tileCount = document.TileCount;
        for (var i = 0; i < tileCount; i++)
        {
            var tile = document.GetTile(i);
            for (var layer = 0; layer < MapDocument.LayerCount; layer++)
            {
                var sheet = tile.GetLayer(layer).Sheet;
                if (sheet < short.MinValue || sheet > short.MaxValue)
                {
                    throw new MapValidationException(MapValidationError.SheetOutOfRange,
                        $"Encode: sheet {sheet} at tile {i}, layer {layer} is outside the Int16 range.");
                }
            }
        }

        var bytes = new byte[HeaderSize + BytesPerTile * tileCount];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, document.Version);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), document.EditorVersion);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), document.Width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), document.Height);

        var offset = HeaderSize;
        for (var i = 0; i < tileCount; i++)
        {
            var tile = document.GetTile(i);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), tile.Flags);
            offset += 4;
            for (var layer = 0; layer < MapDocument.LayerCount; layer++)
            {
                var layerValue = tile.GetLayer(layer);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), layerValue.Graphic);
                offset += 4;
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), (short)layerValue.Sheet);
                offset += 2;
            }
        }

        return bytes;
    }
}
