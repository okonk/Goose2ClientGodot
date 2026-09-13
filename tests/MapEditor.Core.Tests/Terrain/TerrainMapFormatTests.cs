using System;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class TerrainMapFormatTests
{
    private static readonly Guid Grass = TerrainCatalogFixture.Grass;

    private static TerrainMapResolver Resolver()
        => new(TerrainCatalogValidator.Validate(TerrainCatalogFixture.Valid()).Index!);

    [Fact]
    public void TerrainPaint_EncodeDecode_PreservesOrdinaryMapFormat()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));
        var document = session.Document;
        document.SetFlags(0, 0, 0x11223344);
        document.SetLayer(3, 0, 0, new MapTileLayer(2286, 421500));
        document.SetLayer(0, 3, 2, new MapTileLayer(short.MinValue, int.MaxValue));

        var begin = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 1, 1);
        Assert.True(begin.IsActive);
        Assert.True(session.ContinueTerrainStroke(2, 1).IsActive);
        Assert.True(session.CompleteStroke());
        Assert.NotEqual(default, document[1, 1].GetLayer(0));
        Assert.NotEqual(default, document[2, 1].GetLayer(0));

        byte[] encoded = MapCodec.Encode(document);
        Assert.Equal(MapCodec.HeaderSize + MapCodec.BytesPerTile * document.TileCount, encoded.Length);

        MapDocument decoded = MapCodec.Decode(encoded);

        Assert.Equal(document.Version, decoded.Version);
        Assert.Equal(document.EditorVersion, decoded.EditorVersion);
        Assert.Equal(document.Width, decoded.Width);
        Assert.Equal(document.Height, decoded.Height);
        Assert.Equal(document.TileCount, decoded.TileCount);
        for (int i = 0; i < document.TileCount; i++)
        {
            Assert.Equal(document.GetTile(i).Flags, decoded.GetTile(i).Flags);
            for (int layer = 0; layer < MapDocument.LayerCount; layer++)
            {
                Assert.Equal(document.GetTile(i).GetLayer(layer), decoded.GetTile(i).GetLayer(layer));
            }
        }
    }
}
