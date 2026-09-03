using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class MapCodecTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Map10x10.bytes");

    [Fact]
    public void Decode_ExistingFixturePreservesHeaderGridAndBlockedCount()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        var doc = MapCodec.Decode(bytes);

        Assert.Equal(146, doc.Version);
        Assert.Equal(10, doc.EditorVersion);
        Assert.Equal(10, doc.Width);
        Assert.Equal(10, doc.Height);
        Assert.Equal(100, doc.TileCount);
        Assert.Equal(12 + 34 * doc.Width * doc.Height, bytes.Length);

        var t = doc.GetTile(0);
        Assert.Equal(421500, t.GetLayer(0).Graphic);
        Assert.Equal(2286, t.GetLayer(0).Sheet);
        Assert.False(t.IsBlocked);

        var blocked = 0;
        for (var i = 0; i < doc.TileCount; i++)
        {
            if (doc.GetTile(i).IsBlocked)
            {
                blocked++;
            }
        }
        Assert.Equal(6, blocked);
    }

    [Fact]
    public void Decode_UsesLittleEndianAndWireFieldOrder()
    {
        var bytes = new byte[]
        {
            0x34, 0x12, // version 0x1234
            0x0A, 0x00, // editor version 10
            0x01, 0x00, 0x00, 0x00, // width 1
            0x01, 0x00, 0x00, 0x00, // height 1
            0x44, 0x33, 0x22, 0x11, // flags 0x11223344
            0x11, 0x11, 0x11, 0x11, 0x02, 0x01, // graphic 0x11111111, sheet 0x0102
            0x22, 0x22, 0x22, 0x22, 0x04, 0x03, // graphic 0x22222222, sheet 0x0304
            0x33, 0x33, 0x33, 0x33, 0x06, 0x05, // graphic 0x33333333, sheet 0x0506
            0x44, 0x44, 0x44, 0x44, 0x08, 0x07, // graphic 0x44444444, sheet 0x0708
            0x55, 0x55, 0x55, 0x55, 0x0A, 0x09  // graphic 0x55555555, sheet 0x090A
        };

        var doc = MapCodec.Decode(bytes);

        Assert.Equal(0x1234, doc.Version);
        Assert.Equal(10, doc.EditorVersion);
        Assert.Equal(1, doc.Width);
        Assert.Equal(1, doc.Height);

        var tile = doc.GetTile(0);
        Assert.Equal(0x11223344, tile.Flags);
        Assert.Equal(new MapTileLayer(0x0102, 0x11111111), tile.GetLayer(0));
        Assert.Equal(new MapTileLayer(0x0304, 0x22222222), tile.GetLayer(1));
        Assert.Equal(new MapTileLayer(0x0506, 0x33333333), tile.GetLayer(2));
        Assert.Equal(new MapTileLayer(0x0708, 0x44444444), tile.GetLayer(3));
        Assert.Equal(new MapTileLayer(0x090A, 0x55555555), tile.GetLayer(4));
    }

    [Fact]
    public void Decode_RowMajorCoordinates()
    {
        var bytes = new byte[12 + 4 * 34];
        bytes[0] = 0x01; // version 1
        bytes[2] = 0x0A; // editor version 10
        bytes[4] = 0x02; // width 2
        bytes[8] = 0x02; // height 2
        var flags = new[] { 1, 2, 4, 8 };
        for (var i = 0; i < flags.Length; i++)
        {
            bytes[12 + i * 34] = (byte)flags[i];
        }

        var doc = MapCodec.Decode(bytes);

        Assert.Equal(1, doc.GetTile(0).Flags);
        Assert.Equal(2, doc.GetTile(1).Flags);
        Assert.Equal(4, doc.GetTile(2).Flags);
        Assert.Equal(8, doc.GetTile(3).Flags);
        Assert.Equal(1, doc[0, 0].Flags);
        Assert.Equal(2, doc[1, 0].Flags);
        Assert.Equal(4, doc[0, 1].Flags);
        Assert.Equal(8, doc[1, 1].Flags);
    }

    [Fact]
    public void Encode_ExistingFixtureIsByteForByteIdentical()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        var doc = MapCodec.Decode(bytes);

        Assert.Equal(bytes, MapCodec.Encode(doc));
    }

    [Fact]
    public void EncodeDecode_PreservesSignedExtremesAndUnknownFlags()
    {
        var doc = MapDocument.Create(2, 1);
        doc.SetFlags(0, 0, int.MinValue);
        doc.SetFlags(1, 0, int.MaxValue);
        doc.SetLayer(0, 0, 0, new MapTileLayer(1, int.MinValue));
        doc.SetLayer(0, 0, 1, new MapTileLayer(2, int.MaxValue));
        doc.SetLayer(1, 0, 0, new MapTileLayer(short.MinValue, 0));
        doc.SetLayer(1, 0, 1, new MapTileLayer(short.MaxValue, 0));
        doc.SetLayer(1, 0, 4, new MapTileLayer(7, 0));

        var roundTripped = MapCodec.Decode(MapCodec.Encode(doc));

        Assert.Equal(int.MinValue, roundTripped.GetTile(0).Flags);
        Assert.Equal(int.MaxValue, roundTripped.GetTile(1).Flags);
        Assert.Equal(int.MinValue, roundTripped.GetTile(0).GetLayer(0).Graphic);
        Assert.Equal(int.MaxValue, roundTripped.GetTile(0).GetLayer(1).Graphic);
        Assert.Equal(new MapTileLayer(short.MinValue, 0), roundTripped.GetTile(1).GetLayer(0));
        Assert.Equal(new MapTileLayer(short.MaxValue, 0), roundTripped.GetTile(1).GetLayer(1));
        Assert.Equal(new MapTileLayer(7, 0), roundTripped.GetTile(1).GetLayer(4));
    }

    [Fact]
    public void Encode_LengthIsHeaderPlus34BytesPerTile()
    {
        var doc = MapDocument.Create(3, 2);

        Assert.Equal(MapCodec.HeaderSize + MapCodec.BytesPerTile * 6, MapCodec.Encode(doc).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void Decode_RejectsEveryTruncatedHeaderLength(int length)
    {
        var ex = Assert.Throws<MapFormatException>(() => MapCodec.Decode(ValidOneTileBytes().AsSpan(0, length)));

        Assert.Equal(MapFormatError.TruncatedHeader, ex.Error);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(0, 8)]
    [InlineData(-1, 4)]
    [InlineData(-1, 8)]
    [InlineData(-100, 4)]
    [InlineData(-100, 8)]
    public void Decode_RejectsZeroAndNegativeDimensions(int value, int offset)
    {
        var bytes = ValidOneTileBytes();
        WriteInt32LittleEndian(bytes, offset, value);

        var ex = Assert.Throws<MapFormatException>(() => MapCodec.Decode(bytes));

        Assert.Equal(MapFormatError.InvalidDimensions, ex.Error);
    }

    [Theory]
    [InlineData(1001, 4)]
    [InlineData(1001, 8)]
    [InlineData(int.MaxValue, 4)]
    [InlineData(int.MaxValue, 8)]
    public void Decode_RejectsDimensionsAbove1000BeforeAllocation(int value, int offset)
    {
        var bytes = ValidOneTileBytes();
        WriteInt32LittleEndian(bytes, offset, value);

        var ex = Assert.Throws<MapFormatException>(() => MapCodec.Decode(bytes));

        Assert.Equal(MapFormatError.OversizedDimensions, ex.Error);
    }

    [Fact]
    public void Decode_MaximumDimensionsComputesCheckedExpectedLength()
    {
        var bytes = new byte[12];
        bytes[0] = 0x01;
        bytes[2] = 0x0A;
        WriteInt32LittleEndian(bytes, 4, 1000);
        WriteInt32LittleEndian(bytes, 8, 1000);

        var ex = Assert.Throws<MapFormatException>(() => MapCodec.Decode(bytes));

        Assert.Equal(MapFormatError.LengthMismatch, ex.Error);
        Assert.Contains("34000012", ex.Message);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(26)]
    [InlineData(32)]
    [InlineData(38)]
    [InlineData(44)]
    [InlineData(45)]
    public void Decode_RejectsEverySingleByteTruncationBoundary(int length)
    {
        var ex = Assert.Throws<MapFormatException>(() => MapCodec.Decode(ValidOneTileBytes().AsSpan(0, length)));

        Assert.Equal(MapFormatError.LengthMismatch, ex.Error);
    }

    [Fact]
    public void Decode_IgnoresTrailingBytes()
    {
        var baseBytes = ValidOneTileBytes();
        var bytes = new byte[baseBytes.Length + 404];
        Array.Copy(baseBytes, bytes, baseBytes.Length);
        for (var i = baseBytes.Length; i < bytes.Length; i++)
        {
            bytes[i] = 0xFF;
        }

        var document = MapCodec.Decode(bytes);

        Assert.Equal(1, document.Width);
        Assert.Equal(1, document.Height);
        Assert.Equal(0, document.GetTile(0).Flags);
    }

    [Fact]
    public void Encode_DoesNotEmitTrailingBytes()
    {
        var document = MapCodec.Decode(ValidOneTileBytes());

        var encoded = MapCodec.Encode(document);

        Assert.Equal(MapCodec.HeaderSize + MapCodec.BytesPerTile, encoded.Length);
    }

    [Fact]
    public void Decode_AcceptsLegacyEditorVersionAndPreservesIt()
    {
        var bytes = ValidOneTileBytes();
        bytes[2] = 3;

        var document = MapCodec.Decode(bytes);

        Assert.Equal(3, document.EditorVersion);
        Assert.Equal(3, BinaryPrimitives.ReadInt16LittleEndian(MapCodec.Encode(document).AsSpan(2, 2)));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(11)]
    public void Decode_RejectsUnsupportedEditorLayoutWithoutRewriting(int editorVersion)
    {
        var bytes = ValidOneTileBytes();
        bytes[2] = (byte)editorVersion;

        var ex = Assert.Throws<MapFormatException>(() => MapCodec.Decode(bytes));

        Assert.Equal(MapFormatError.UnsupportedEditorVersion, ex.Error);

        var accepted = ValidOneTileBytes();
        accepted[0] = (byte)146;
        var doc = MapCodec.Decode(accepted);
        Assert.Equal(146, doc.Version);
        Assert.Equal(10, doc.EditorVersion);
    }

    [Theory]
    [InlineData(32768)]
    [InlineData(-32769)]
    public void Encode_RejectsSheetOutsideInt16WithoutWriting(int sheet)
    {
        var doc = MapDocument.Create(1, 1);
        doc.SetLayer(0, 0, 2, new MapTileLayer(sheet, 5));

        var ex = Assert.Throws<MapValidationException>(() => MapCodec.Encode(doc));

        Assert.Equal(MapValidationError.SheetOutOfRange, ex.Error);
    }

    [Fact]
    public void DecodeFailure_DoesNotProducePartialDocument()
    {
        var decode = typeof(MapCodec).GetMethod("Decode", BindingFlags.Public | BindingFlags.Static, new[] { typeof(ReadOnlySpan<byte>) });
        Assert.NotNull(decode);
        Assert.Equal(typeof(MapDocument), decode!.ReturnType);
        Assert.DoesNotContain(decode.GetParameters(), p => p.IsOut);

        var truncated = new byte[45];
        Array.Copy(ValidOneTileBytes(), truncated, 45);
        Assert.Throws<MapFormatException>(() => MapCodec.Decode(truncated));
    }

    private static byte[] ValidOneTileBytes()
    {
        var bytes = new byte[12 + 34];
        bytes[0] = 0x01; // version 1
        bytes[2] = 0x0A; // editor version 10
        bytes[4] = 0x01; // width 1
        bytes[8] = 0x01; // height 1
        return bytes;
    }

    private static void WriteInt32LittleEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
