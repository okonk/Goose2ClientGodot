using MapEditor.Core;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.Core.Tests.Terrain;

public class TerrainMapFormatTests
{
    [Fact]
    public void TerrainEdit_EncodeDecodeUsesUnchangedHeader34ByteTilesAndOrdinaryLayerReferences()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var document = MapDocument.Create(3, 2);
        document.SetFlags(1, 1, 123);
        document.SetLayer(2, 1, 4, new MapTileLayer(4, 5));
        var session = new MapEditSession(document);
        session.SelectedLayers = 1 << 2;
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.ContinueTerrainStroke(2, 0);
        session.CompleteStroke();

        var bytes = MapCodec.Encode(document);
        var decoded = MapCodec.Decode(bytes);

        Assert.Equal(MapCodec.HeaderSize + MapCodec.BytesPerTile * document.TileCount, bytes.Length);
        for (var i = 0; i < document.TileCount; i++)
        {
            Assert.Equal(document.GetTile(i).Flags, decoded.GetTile(i).Flags);
            for (var layer = 0; layer < MapDocument.LayerCount; layer++)
            {
                Assert.Equal(document.GetTile(i).GetLayer(layer), decoded.GetTile(i).GetLayer(layer));
            }
        }
    }
}
