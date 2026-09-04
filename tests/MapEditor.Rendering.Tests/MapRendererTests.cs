using System;
using System.IO;
using System.Linq;
using MapEditor.Core;
using MapEditor.Rendering;
using MapEditor.Rendering.Tests.Fakes;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class MapRendererTests
{
    private const string AssetDirectory = "test-assets";

    [Fact]
    public void Render_EmitsLayersZeroThroughFourRegardlessOfReferenceNumericOrder()
    {
        string manifest = ManifestJson(
            (1, 1, 0, 0, 32, 32),
            (1, 2, 0, 0, 32, 32),
            (1, 3, 0, 0, 32, 32),
            (1, 4, 0, 0, 32, 32),
            (1, 5, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(1, 1);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            document.SetLayer(0, 0, layer, new MapTileLayer(1, MapDocument.LayerCount - layer));
        }

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(5, sink.CallCount);
        (int Layer, int Graphic)[] expected =
        {
            (0, 5), (1, 4), (2, 3), (3, 2), (4, 1)
        };
        for (int i = 0; i < expected.Length; i++)
        {
            SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[i];
            Assert.Equal(expected[i].Layer, operation.Layer);
            Assert.Equal(new MapTileCoordinate(0, 0), operation.Tile);
            Assert.Equal(new SpriteReference(1, expected[i].Graphic), operation.Reference);
        }
    }

    [Fact]
    public void Render_WithinLayerUsesRowMajorTileOrder()
    {
        string manifest = ManifestJson(
            (1, 1, 0, 0, 32, 32),
            (1, 2, 0, 0, 32, 32),
            (1, 3, 0, 0, 32, 32),
            (1, 4, 0, 0, 32, 32),
            (1, 5, 0, 0, 32, 32),
            (1, 6, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(3, 2);
        (int X, int Y)[] cells = { (0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1) };
        for (int i = 0; i < cells.Length; i++)
        {
            document.SetLayer(cells[i].X, cells[i].Y, 0, new MapTileLayer(1, i + 1));
        }

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(96, 64)), sink);

        Assert.Equal(6, sink.CallCount);
        for (int i = 0; i < cells.Length; i++)
        {
            SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[i];
            Assert.Equal(new MapTileCoordinate(cells[i].X, cells[i].Y), operation.Tile);
            Assert.Equal(new SpriteReference(1, i + 1), operation.Reference);
        }
    }

    [Fact]
    public void Render_HiddenLayerIsNotReadForAssetResolutionOrEmitted()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32), (2, 1, 0, 0, 32, 32));
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(loader, manifest);
        MapRenderer renderer = new(cache);
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        document.SetLayer(0, 0, 1, new MapTileLayer(2, 1));
        MapRenderOptions options = new(new MapLayerVisibility(0b00001), false, false, null, null);

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32), options), sink);

        Assert.Equal(1, sink.CallCount);
        SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[0];
        Assert.Equal(0, operation.Layer);
        Assert.Equal(new SpriteReference(1, 1), operation.Reference);
        Assert.Equal(1, loader.CallCount);
        Assert.Equal(Path.Combine(cache.AssetDirectory, "sheets", "1.png"), loader.LoadedPaths[0]);
    }

    [Fact]
    public void Render_UsesExactManifestSourceRect()
    {
        string manifest = ManifestJson((1, 1, 13, 29, 40, 24));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(1, sink.CallCount);
        SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[0];
        Assert.Equal(new SpriteSourceRect(13, 29, 40, 24), operation.SourceRect);
        Assert.Equal(new RenderRect(-4, 8, 40, 24), operation.DestinationRect);
    }

    [Fact]
    public void Render_BottomCenterAnchors32x32And48x64Frames()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32), (1, 2, 0, 0, 48, 64));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(4, 2);
        document.SetLayer(2, 1, 0, new MapTileLayer(1, 1));
        document.SetLayer(3, 1, 0, new MapTileLayer(1, 2));
        ViewportTransform viewport = Viewport(200, 200, 10, 5, MapZoom.Percent200);

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, viewport), sink);

        Assert.Equal(2, sink.CallCount);
        SpriteDrawOperation small = (SpriteDrawOperation)sink.Calls[0];
        SpriteDrawOperation large = (SpriteDrawOperation)sink.Calls[1];
        Assert.Equal(new MapTileCoordinate(2, 1), small.Tile);
        Assert.Equal(new RenderRect(108, 54, 64, 64), small.DestinationRect);
        Assert.Equal(new MapTileCoordinate(3, 1), large.Tile);
        Assert.Equal(new RenderRect(156, -10, 96, 128), large.DestinationRect);
    }

    [Fact]
    public void Render_DestinationScalesAtEveryZoomWithoutChangingSourceRect()
    {
        string manifest = ManifestJson((1, 1, 5, 7, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));

        (MapZoom Zoom, double Scale)[] zooms =
        {
            (MapZoom.Percent25, 0.25),
            (MapZoom.Percent50, 0.5),
            (MapZoom.Percent100, 1.0),
            (MapZoom.Percent200, 2.0),
            (MapZoom.Percent400, 4.0)
        };

        foreach ((MapZoom zoom, double scale) in zooms)
        {
            ViewportTransform viewport = Viewport(32 * scale, 32 * scale, 0, 0, zoom);
            RecordingMapDrawSink sink = new();
            renderer.Render(Request(document, viewport), sink);
            Assert.Equal(1, sink.CallCount);
            SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[0];
            Assert.Equal(new SpriteSourceRect(5, 7, 32, 32), operation.SourceRect);
            Assert.Equal(new RenderRect(0, 0, 32 * scale, 32 * scale), operation.DestinationRect);
        }
    }

    [Fact]
    public void Render_AlwaysRequestsNearestNeighborSampling()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32), (1, 2, 0, 0, 48, 64));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(2, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        document.SetLayer(1, 0, 0, new MapTileLayer(1, 2));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(64, 32)), sink);

        Assert.Equal(2, sink.CallCount);
        Assert.All(sink.Calls.OfType<SpriteDrawOperation>(),
            operation => Assert.Equal(SpriteSampling.NearestNeighbor, operation.Sampling));
    }

    [Fact]
    public void Render_CullsCellOutsideViewport()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        FakeSpriteSheetLoader loader = new();
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(2, 2);
        document.SetLayer(1, 1, 0, new MapTileLayer(1, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(0, sink.CallCount);
        Assert.Equal(0, loader.CallCount);
    }

    [Fact]
    public void Render_TallSpriteAnchoredOneRowBelowVisibleCellsWhenItOverlaps()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 96));
        FakeSpriteSheetLoader loader = new(_ => SpriteSheetLoadResult.Success(new FakeSpriteSheetImage(64, 96)));
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(1, 3);
        document.SetLayer(0, 1, 0, new MapTileLayer(1, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(1, sink.CallCount);
        SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[0];
        Assert.Equal(new MapTileCoordinate(0, 1), operation.Tile);
        Assert.Equal(new RenderRect(0, -32, 32, 96), operation.DestinationRect);
    }

    [Fact]
    public void Render_ConservativeCandidateThatIntersectsByHalfPixelEmitsSprite()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 33, 32));
        FakeSpriteSheetLoader loader = new();
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(2, 1);
        document.SetLayer(1, 0, 0, new MapTileLayer(1, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(1, sink.CallCount);
        SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[0];
        Assert.Equal(new MapTileCoordinate(1, 0), operation.Tile);
        Assert.Equal(new RenderRect(31.5, 0, 33, 32), operation.DestinationRect);
        Assert.Equal(1, loader.CallCount);
    }

    [Fact]
    public void Render_OddWidthFrameAnchorsAtHalfPixelAndIntersectsEdge()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 33, 33));
        FakeSpriteSheetLoader loader = new();
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(2, 1);
        document.SetLayer(1, 0, 0, new MapTileLayer(1, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(1, sink.CallCount);
        SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[0];
        Assert.Equal(new MapTileCoordinate(1, 0), operation.Tile);
        Assert.Equal(new RenderRect(31.5, -1, 33, 33), operation.DestinationRect);
        Assert.Equal(1, loader.CallCount);
    }

    [Fact]
    public void Render_GiantValidFrameOnSmallMapRendersWithoutManifestDimensionCap()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 100000, 100000));
        FakeSpriteSheetLoader loader = new(_ => SpriteSheetLoadResult.Success(new FakeSpriteSheetImage(100000, 100000)));
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(2, 2);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(1, sink.CallCount);
        SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[0];
        Assert.Equal(new SpriteSourceRect(0, 0, 100000, 100000), operation.SourceRect);
        Assert.Equal(new RenderRect(-49984, -99968, 100000, 100000), operation.DestinationRect);
        Assert.Equal(1, loader.CallCount);
    }

    [Fact]
    public void Render_SeesDocumentSetLayerImmediatelyWithoutRendererRecreation()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(1, 1);
        MapEditSession session = new(document);

        RecordingMapDrawSink emptySink = new();
        renderer.Render(Request(document, Viewport(32, 32)), emptySink);
        Assert.Equal(0, emptySink.CallCount);

        session.SelectedLayers = 1 << 2;
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());

        RecordingMapDrawSink paintedSink = new();
        renderer.Render(Request(document, Viewport(32, 32)), paintedSink);
        Assert.Equal(1, paintedSink.CallCount);
        SpriteDrawOperation operation = (SpriteDrawOperation)paintedSink.Calls[0];
        Assert.Equal(2, operation.Layer);
        Assert.Equal(new SpriteReference(1, 1), operation.Reference);

        Assert.True(session.Undo());

        RecordingMapDrawSink undoneSink = new();
        renderer.Render(Request(document, Viewport(32, 32)), undoneSink);
        Assert.Equal(0, undoneSink.CallCount);
        Assert.Equal(new MapTileLayer(0, 0), document[0, 0].GetLayer(2));
    }

    [Fact]
    public void LayerVisibility_ValidatesFiveBitsAndLayerIndexesWithoutMutation()
    {
        Assert.Equal(0b11111, MapLayerVisibility.All.Mask);
        Assert.Equal(0b00000, MapLayerVisibility.None.Mask);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            Assert.True(MapLayerVisibility.All.IsVisible(layer));
            Assert.False(MapLayerVisibility.None.IsVisible(layer));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new MapLayerVisibility(0b111110));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapLayerVisibility(0b111111));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayerVisibility.All.IsVisible(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayerVisibility.All.IsVisible(MapDocument.LayerCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayerVisibility.None.WithVisibility(-1, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayerVisibility.None.WithVisibility(MapDocument.LayerCount, true));

        MapLayerVisibility original = MapLayerVisibility.All;
        MapLayerVisibility hidden = original.WithVisibility(2, false);
        Assert.Equal(0b11111, original.Mask);
        Assert.Equal(0b11011, hidden.Mask);
        Assert.False(hidden.IsVisible(2));
        Assert.True(hidden.WithVisibility(2, true).IsVisible(2));
    }

    [Fact]
    public void Render_GraphicZeroEmitsNothingEvenWithNonzeroSheet()
    {
        string manifest = ManifestJson((7, 1, 0, 0, 32, 32));
        FakeSpriteSheetLoader loader = new();
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(7, 0));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(0, sink.CallCount);
        Assert.Equal(0, loader.CallCount);
    }

    [Fact]
    public void Render_NonzeroGraphicWithZeroOrNegativeSheetEmitsUnknownSheetPlaceholder()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        FakeSpriteSheetLoader loader = new();
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(2, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(0, 5));
        document.SetLayer(1, 0, 0, new MapTileLayer(-3, 5));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(64, 32)), sink);

        Assert.Equal(2, sink.CallCount);
        PlaceholderDrawOperation zero = (PlaceholderDrawOperation)sink.Calls[0];
        Assert.Equal(new SpriteReference(0, 5), zero.Reference);
        Assert.Equal(SpriteResolutionStatus.UnknownSheet, zero.Reason);
        Assert.Equal(new MapTileCoordinate(0, 0), zero.Tile);
        Assert.False(string.IsNullOrEmpty(zero.Diagnostic));
        PlaceholderDrawOperation negative = (PlaceholderDrawOperation)sink.Calls[1];
        Assert.Equal(new SpriteReference(-3, 5), negative.Reference);
        Assert.Equal(SpriteResolutionStatus.UnknownSheet, negative.Reason);
        Assert.Equal(new MapTileCoordinate(1, 0), negative.Tile);
        Assert.False(string.IsNullOrEmpty(negative.Diagnostic));
        Assert.Equal(0, loader.CallCount);
    }

    [Fact]
    public void Render_UnknownGraphicMissingFileLoadFailureAndOutsideFrameEmitReasonedPlaceholders()
    {
        string manifest = ManifestJson(
            (1, 1, 0, 0, 32, 32),
            (2, 1, 0, 0, 32, 32),
            (3, 1, 0, 0, 32, 32),
            (4, 1, 40, 40, 64, 64));
        FakeSpriteSheetLoader loader = new(path =>
        {
            if (path.EndsWith("2.png"))
            {
                return SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, "file missing");
            }

            if (path.EndsWith("3.png"))
            {
                return SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.Unreadable, "corrupt data");
            }

            return SpriteSheetLoadResult.Success(new FakeSpriteSheetImage(64, 64));
        });
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(4, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 99));
        document.SetLayer(1, 0, 1, new MapTileLayer(2, 1));
        document.SetLayer(2, 0, 2, new MapTileLayer(3, 1));
        document.SetLayer(3, 0, 3, new MapTileLayer(4, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(128, 32)), sink);

        Assert.Equal(4, sink.CallCount);
        (SpriteResolutionStatus Reason, int Layer, int X, int Sheet, int Graphic)[] expected =
        {
            (SpriteResolutionStatus.UnknownGraphic, 0, 0, 1, 99),
            (SpriteResolutionStatus.MissingSheetFile, 1, 1, 2, 1),
            (SpriteResolutionStatus.SheetLoadFailed, 2, 2, 3, 1),
            (SpriteResolutionStatus.FrameOutsideSheet, 3, 3, 4, 1)
        };
        for (int i = 0; i < expected.Length; i++)
        {
            PlaceholderDrawOperation operation = (PlaceholderDrawOperation)sink.Calls[i];
            Assert.Equal(expected[i].Reason, operation.Reason);
            Assert.Equal(expected[i].Layer, operation.Layer);
            Assert.Equal(new MapTileCoordinate(expected[i].X, 0), operation.Tile);
            Assert.Equal(new SpriteReference(expected[i].Sheet, expected[i].Graphic), operation.Reference);
            Assert.False(string.IsNullOrEmpty(operation.Diagnostic));
        }
    }

    [Fact]
    public void Render_PlaceholderPreservesExactLayerTileAndSignedReference()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 3, new MapTileLayer(-5, -9));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(1, sink.CallCount);
        PlaceholderDrawOperation operation = (PlaceholderDrawOperation)sink.Calls[0];
        Assert.Equal(3, operation.Layer);
        Assert.Equal(new MapTileCoordinate(0, 0), operation.Tile);
        Assert.Equal(new SpriteReference(-5, -9), operation.Reference);
        Assert.Equal(SpriteResolutionStatus.UnknownSheet, operation.Reason);
    }

    [Fact]
    public void Render_PlaceholderUsesCellRectRatherThanManifestFrameRect()
    {
        string manifest = ManifestJson((1, 1, 40, 40, 48, 64));
        FakeSpriteSheetLoader loader = new(_ => SpriteSheetLoadResult.Success(new FakeSpriteSheetImage(64, 64)));
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(1, sink.CallCount);
        PlaceholderDrawOperation operation = (PlaceholderDrawOperation)sink.Calls[0];
        Assert.Equal(SpriteResolutionStatus.FrameOutsideSheet, operation.Reason);
        Assert.Equal(new RenderRect(0, 0, 32, 32), operation.DestinationRect);
    }

    [Fact]
    public void Render_OffscreenMissingReferenceDoesNotLoadOrEmitPlaceholder()
    {
        string manifest = ManifestJson((9, 1, 0, 0, 32, 32), (9, 2, 0, 0, 32, 64));
        FakeSpriteSheetLoader loader = new();
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(1, 2);
        document.SetLayer(0, 1, 0, new MapTileLayer(9, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(0, sink.CallCount);
        Assert.Equal(0, loader.CallCount);
    }

    [Fact]
    public void Render_PlaceholderPaletteIsConspicuousAndLocked()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(8, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(1, sink.CallCount);
        PlaceholderDrawOperation operation = (PlaceholderDrawOperation)sink.Calls[0];
        Assert.Equal(new RenderColor(0xFF, 0x00, 0xFF, 0xCC), operation.FillColor);
        Assert.Equal(new RenderColor(0xFF, 0xFF, 0x00, 0xFF), operation.StrokeColor);
    }

    [Fact]
    public void Render_DoesNotChangeDocumentBytesOrEditSessionDirtyHistory()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(2, 2);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        document.SetLayer(1, 0, 1, new MapTileLayer(9, 1));
        document.SetFlags(0, 1, MapDocument.BlockedFlag);
        MapEditSession session = new(document);
        byte[] before = MapCodec.Encode(document);
        MapRenderOptions options = new(
            MapLayerVisibility.All,
            true,
            true,
            new MapTileCoordinate(0, 0),
            new MapTileCoordinate(1, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(64, 64), options), sink);

        Assert.NotEmpty(sink.Calls);
        Assert.Equal(before, MapCodec.Encode(document));
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(new MapTileLayer(1, 1), document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(9, 1), document[1, 0].GetLayer(1));
        Assert.Equal(MapDocument.BlockedFlag, document[0, 1].Flags);
    }

    [Fact]
    public void Render_BlockedOverlayUsesOnlyBlockedBitAndVisibleCells()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(3, 1);
        document.SetFlags(0, 0, MapDocument.BlockedFlag);
        document.SetFlags(1, 0, unchecked((int)0x80000002));
        document.SetFlags(2, 0, unchecked((int)0x80000000));
        MapRenderOptions options = new(MapLayerVisibility.All, false, true, null, null);

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(96, 32), options), sink);

        Assert.Equal(2, sink.CallCount);
        CellOverlayDrawOperation first = (CellOverlayDrawOperation)sink.Calls[0];
        CellOverlayDrawOperation second = (CellOverlayDrawOperation)sink.Calls[1];
        Assert.Equal(CellOverlayKind.Blocked, first.Kind);
        Assert.Equal(new MapTileCoordinate(0, 0), first.Tile);
        Assert.Equal(new RenderRect(0, 0, 32, 32), first.DestinationRect);
        Assert.Equal(new RenderColor(0xFF, 0x00, 0x00, 0x60), first.FillColor);
        Assert.Equal(new RenderColor(0, 0, 0, 0), first.StrokeColor);
        Assert.Equal(CellOverlayKind.Blocked, second.Kind);
        Assert.Equal(new MapTileCoordinate(1, 0), second.Tile);
        Assert.Equal(new RenderRect(32, 0, 32, 32), second.DestinationRect);
    }

    [Fact]
    public void Render_HiddenLayersDoNotHideBlockedOverlay()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        FakeSpriteSheetLoader loader = new();
        MapRenderer renderer = new(CreateCache(loader, manifest));
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        document.SetFlags(0, 0, MapDocument.BlockedFlag);
        MapRenderOptions options = new(MapLayerVisibility.None, false, true, null, null);

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32), options), sink);

        Assert.Equal(1, sink.CallCount);
        CellOverlayDrawOperation operation = (CellOverlayDrawOperation)sink.Calls[0];
        Assert.Equal(CellOverlayKind.Blocked, operation.Kind);
        Assert.Equal(new MapTileCoordinate(0, 0), operation.Tile);
        Assert.Equal(0, loader.CallCount);
    }

    [Fact]
    public void Render_GridEmitsUniqueVisibleBoundaryLinesOnly()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));

        MapDocument oneCell = MapDocument.Create(1, 1);
        RecordingMapDrawSink singleSink = new();
        renderer.Render(Request(oneCell, Viewport(32, 32), GridOptions()), singleSink);
        Assert.Equal(
            SortLines(
                (new RenderPoint(0, 0), new RenderPoint(0, 32)),
                (new RenderPoint(32, 0), new RenderPoint(32, 32)),
                (new RenderPoint(0, 0), new RenderPoint(32, 0)),
                (new RenderPoint(0, 32), new RenderPoint(32, 32))),
            SortLines(AsLines(singleSink.Calls)));

        MapDocument tenByTen = MapDocument.Create(10, 10);
        ViewportTransform partial = Viewport(64, 36.5, 10.5, 30.25);
        RecordingMapDrawSink partialSink = new();
        renderer.Render(Request(tenByTen, partial, GridOptions()), partialSink);
        Assert.Equal(
            SortLines(
                (new RenderPoint(21.5, 0), new RenderPoint(21.5, 36.5)),
                (new RenderPoint(53.5, 0), new RenderPoint(53.5, 36.5)),
                (new RenderPoint(0, 1.75), new RenderPoint(64, 1.75)),
                (new RenderPoint(0, 33.75), new RenderPoint(64, 33.75))),
            SortLines(AsLines(partialSink.Calls)));
    }

    [Fact]
    public void Render_GridLinesAreClippedToVisibleMapIntersection()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(10, 10);
        ViewportTransform viewport = Viewport(60, 60, -10, -10);

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, viewport, GridOptions()), sink);

        Assert.Equal(
            SortLines(
                (new RenderPoint(10, 10), new RenderPoint(10, 60)),
                (new RenderPoint(42, 10), new RenderPoint(42, 60)),
                (new RenderPoint(10, 10), new RenderPoint(60, 10)),
                (new RenderPoint(10, 42), new RenderPoint(60, 42))),
            SortLines(AsLines(sink.Calls)));
    }

    [Fact]
    public void Render_SelectedThenHoveredAreLastAndCanShareCell()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        document.SetFlags(0, 0, MapDocument.BlockedFlag);
        MapRenderOptions options = new(
            MapLayerVisibility.All,
            true,
            true,
            new MapTileCoordinate(0, 0),
            new MapTileCoordinate(0, 0));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32), options), sink);

        Assert.Equal(8, sink.CallCount);
        Assert.IsType<SpriteDrawOperation>(sink.Calls[0]);
        Assert.IsType<CellOverlayDrawOperation>(sink.Calls[1]);
        Assert.All(sink.Calls.Skip(2).Take(4), call => Assert.IsType<GridLineDrawOperation>(call));
        CellOverlayDrawOperation selected = (CellOverlayDrawOperation)sink.Calls[6];
        CellOverlayDrawOperation hovered = (CellOverlayDrawOperation)sink.Calls[7];
        Assert.Equal(CellOverlayKind.Selected, selected.Kind);
        Assert.Equal(CellOverlayKind.Hovered, hovered.Kind);
        Assert.Equal(new MapTileCoordinate(0, 0), selected.Tile);
        Assert.Equal(new MapTileCoordinate(0, 0), hovered.Tile);
        Assert.Equal(new RenderRect(0, 0, 32, 32), selected.DestinationRect);
        Assert.Equal(new RenderRect(0, 0, 32, 32), hovered.DestinationRect);
        Assert.Equal(new RenderColor(0, 0, 0, 0), selected.FillColor);
        Assert.Equal(new RenderColor(0x00, 0xFF, 0xFF, 0xFF), selected.StrokeColor);
        Assert.Equal(new RenderColor(0, 0, 0, 0), hovered.FillColor);
        Assert.Equal(new RenderColor(0xFF, 0x00, 0xFF, 0xFF), hovered.StrokeColor);
    }

    [Fact]
    public void Render_OutOfMapOrOffscreenTargetsEmitNothing()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(2, 2);

        MapRenderOptions outOfMap = new(
            MapLayerVisibility.All,
            false,
            false,
            new MapTileCoordinate(-1, 0),
            new MapTileCoordinate(5, 5));
        RecordingMapDrawSink outOfMapSink = new();
        renderer.Render(Request(document, Viewport(64, 64), outOfMap), outOfMapSink);
        Assert.Equal(0, outOfMapSink.CallCount);

        MapRenderOptions offscreen = new(
            MapLayerVisibility.All,
            false,
            false,
            new MapTileCoordinate(0, 0),
            new MapTileCoordinate(1, 1));
        RecordingMapDrawSink offscreenSink = new();
        renderer.Render(Request(document, Viewport(32, 32, 100, 100), offscreen), offscreenSink);
        Assert.Equal(0, offscreenSink.CallCount);
    }

    [Fact]
    public void Render_DefaultOptionsShowAllLayersAndNoOverlays()
    {
        MapRenderOptions defaults = MapRenderOptions.Default;
        Assert.Equal(0b11111, defaults.VisibleLayers.Mask);
        Assert.False(defaults.ShowGrid);
        Assert.False(defaults.ShowBlocked);
        Assert.Null(defaults.HoveredTile);
        Assert.Null(defaults.SelectedTile);

        string manifest = ManifestJson(
            (1, 1, 0, 0, 32, 32),
            (1, 2, 0, 0, 32, 32),
            (1, 3, 0, 0, 32, 32),
            (1, 4, 0, 0, 32, 32),
            (1, 5, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(1, 1);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            document.SetLayer(0, 0, layer, new MapTileLayer(1, layer + 1));
        }

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32), MapRenderOptions.Default), sink);

        Assert.Equal(5, sink.CallCount);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[layer];
            Assert.Equal(layer, operation.Layer);
            Assert.Equal(new SpriteReference(1, layer + 1), operation.Reference);
        }
    }

    [Fact]
    public void Render_OperationOrderIsFiveLayersThenBlockedGridSelectedHovered()
    {
        string manifest = ManifestJson(
            (1, 1, 0, 0, 32, 32),
            (1, 2, 0, 0, 32, 32),
            (1, 3, 0, 0, 32, 32),
            (1, 4, 0, 0, 32, 32),
            (1, 5, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(2, 1);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            document.SetLayer(0, 0, layer, new MapTileLayer(1, layer + 1));
            document.SetLayer(1, 0, layer, new MapTileLayer(1, layer + 1));
            document.SetFlags(0, 0, MapDocument.BlockedFlag);
            document.SetFlags(1, 0, MapDocument.BlockedFlag);
        }
        MapRenderOptions options = new(
            MapLayerVisibility.All,
            true,
            true,
            new MapTileCoordinate(1, 0),
            new MapTileCoordinate(0, 0));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(64, 32), options), sink);

        Assert.Equal(19, sink.CallCount);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            SpriteDrawOperation first = (SpriteDrawOperation)sink.Calls[layer * 2];
            SpriteDrawOperation second = (SpriteDrawOperation)sink.Calls[layer * 2 + 1];
            Assert.Equal(layer, first.Layer);
            Assert.Equal(new MapTileCoordinate(0, 0), first.Tile);
            Assert.Equal(layer, second.Layer);
            Assert.Equal(new MapTileCoordinate(1, 0), second.Tile);
        }

        CellOverlayDrawOperation blockedFirst = (CellOverlayDrawOperation)sink.Calls[10];
        CellOverlayDrawOperation blockedSecond = (CellOverlayDrawOperation)sink.Calls[11];
        Assert.Equal(CellOverlayKind.Blocked, blockedFirst.Kind);
        Assert.Equal(new MapTileCoordinate(0, 0), blockedFirst.Tile);
        Assert.Equal(CellOverlayKind.Blocked, blockedSecond.Kind);
        Assert.Equal(new MapTileCoordinate(1, 0), blockedSecond.Tile);
        Assert.All(sink.Calls.Skip(12).Take(5), call => Assert.IsType<GridLineDrawOperation>(call));
        CellOverlayDrawOperation selected = (CellOverlayDrawOperation)sink.Calls[17];
        CellOverlayDrawOperation hovered = (CellOverlayDrawOperation)sink.Calls[18];
        Assert.Equal(CellOverlayKind.Selected, selected.Kind);
        Assert.Equal(new MapTileCoordinate(0, 0), selected.Tile);
        Assert.Equal(CellOverlayKind.Hovered, hovered.Kind);
        Assert.Equal(new MapTileCoordinate(1, 0), hovered.Tile);
    }

    [Fact]
    public void Render_1000x1000SparseMapReadsOnlyCandidateReferences()
    {
        string manifest = ManifestJson(
            (1, 1, 0, 0, 32, 32),
            (2, 1, 0, 0, 32, 32),
            (3, 1, 0, 0, 32, 32));
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(loader, manifest);
        MapRenderer renderer = new(cache);
        MapDocument document = MapDocument.Create(1000, 1000);
        document.SetLayer(4, 4, 0, new MapTileLayer(1, 1));
        document.SetLayer(900, 900, 0, new MapTileLayer(2, 1));
        document.SetLayer(950, 10, 0, new MapTileLayer(3, 1));
        document.SetLayer(10, 990, 0, new MapTileLayer(2, 1));
        ViewportTransform viewport = Viewport(32, 32, 128, 128);

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, viewport), sink);

        Assert.Equal(1, sink.CallCount);
        SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[0];
        Assert.Equal(new MapTileCoordinate(4, 4), operation.Tile);
        Assert.Equal(new SpriteReference(1, 1), operation.Reference);
        Assert.Equal(1, loader.CallCount);
        Assert.Equal(Path.Combine(cache.AssetDirectory, "sheets", "1.png"), loader.LoadedPaths[0]);
    }

    [Fact]
    public void Render_GiantFrameCandidateRangeAboveBudgetThrowsBeforeReadsResolutionOrSinkCalls()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 500000, 500000));
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(loader, manifest);
        MapRenderer renderer = new(cache);
        MapDocument document = MapDocument.Create(1000, 1000);
        document.SetLayer(999, 999, 0, new MapTileLayer(1, 1));
        document.SetLayer(500, 500, 0, new MapTileLayer(1, 1));
        document.SetLayer(999, 0, 0, new MapTileLayer(1, 1));
        ViewportTransform viewport = Viewport(32, 32);

        RecordingMapDrawSink sink = new();
        MapRenderWorkLimitException exception = Assert.Throws<MapRenderWorkLimitException>(
            () => renderer.Render(Request(document, viewport), sink));

        Assert.Equal(5_000_000, exception.RequestedTileReads);
        Assert.Equal(MapRenderer.MaximumTileReadsPerRender, exception.MaximumTileReads);
        Assert.Equal(0, loader.CallCount);
        Assert.Equal(0, sink.CallCount);
    }

    [Fact]
    public void Render_WorkBudgetCountsVisibleLayersAndBlockedReadsButNotHiddenLayersOrGrid()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(400, 400);
        ViewportTransform viewport = Viewport(12800, 12800);
        MapLayerVisibility fourLayers = MapLayerVisibility.All.WithVisibility(4, false);

        RecordingMapDrawSink atLimitSink = new();
        renderer.Render(Request(document, viewport, new MapRenderOptions(MapLayerVisibility.All, false, false, null, null)), atLimitSink);
        Assert.Equal(0, atLimitSink.CallCount);

        RecordingMapDrawSink hiddenLayersSink = new();
        renderer.Render(Request(document, viewport, new MapRenderOptions(fourLayers, false, true, null, null)), hiddenLayersSink);
        Assert.Equal(0, hiddenLayersSink.CallCount);

        RecordingMapDrawSink gridSink = new();
        renderer.Render(Request(document, viewport, new MapRenderOptions(MapLayerVisibility.All, true, false, null, null)), gridSink);
        Assert.All(gridSink.Calls, call => Assert.IsType<GridLineDrawOperation>(call));

        RecordingMapDrawSink overBudgetSink = new();
        MapRenderWorkLimitException exception = Assert.Throws<MapRenderWorkLimitException>(
            () => renderer.Render(Request(document, viewport, new MapRenderOptions(MapLayerVisibility.All, false, true, null, null)), overBudgetSink));
        Assert.Equal(960_000, exception.RequestedTileReads);
        Assert.Equal(MapRenderer.MaximumTileReadsPerRender, exception.MaximumTileReads);
        Assert.Equal(0, overBudgetSink.CallCount);
    }

    [Fact]
    public void Render_NullDependenciesThrowBeforeSinkCalls()
    {
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), ManifestJson((1, 1, 0, 0, 32, 32))));
        MapDocument document = MapDocument.Create(1, 1);
        ViewportTransform viewport = Viewport(32, 32);

        RecordingMapDrawSink sink = new();
        Assert.Throws<ArgumentNullException>(() => renderer.Render(new MapRenderRequest(null!, viewport, MapRenderOptions.Default), sink));
        Assert.Throws<ArgumentNullException>(() => renderer.Render(new MapRenderRequest(document, viewport, null!), sink));
        Assert.Throws<ArgumentNullException>(() => renderer.Render(Request(document, viewport), null!));
        Assert.Equal(0, sink.CallCount);
        Assert.Throws<ArgumentNullException>(() => new MapRenderer(null!));
    }

    [Fact]
    public void Render_UnavailableAssetsEmitAssetsUnavailablePlaceholdersForNonEmptyReferences()
    {
        SpriteAssetCache cache = SpriteAssetCache.CreateUnavailable();
        MapRenderer renderer = new(cache);
        MapDocument document = MapDocument.Create(3, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 5));
        document.SetLayer(1, 0, 1, new MapTileLayer(0, -7));
        document.SetLayer(2, 0, 2, new MapTileLayer(-3, 9));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(96, 32)), sink);

        Assert.Equal(3, sink.CallCount);
        (int Layer, int X, int Sheet, int Graphic)[] expected =
        {
            (0, 0, 1, 5), (1, 1, 0, -7), (2, 2, -3, 9)
        };
        for (int i = 0; i < expected.Length; i++)
        {
            PlaceholderDrawOperation operation = (PlaceholderDrawOperation)sink.Calls[i];
            Assert.Equal(SpriteResolutionStatus.AssetsUnavailable, operation.Reason);
            Assert.Equal(expected[i].Layer, operation.Layer);
            Assert.Equal(new MapTileCoordinate(expected[i].X, 0), operation.Tile);
            Assert.Equal(new SpriteReference(expected[i].Sheet, expected[i].Graphic), operation.Reference);
            Assert.False(string.IsNullOrEmpty(operation.Diagnostic));
        }
    }

    [Fact]
    public void Render_UnavailableAssetsGraphicZeroEmitsNothing()
    {
        SpriteAssetCache cache = SpriteAssetCache.CreateUnavailable();
        MapRenderer renderer = new(cache);
        MapDocument document = MapDocument.Create(2, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(9, 0));
        document.SetLayer(1, 0, 1, new MapTileLayer(-4, 0));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(64, 32)), sink);

        Assert.Equal(0, sink.CallCount);
    }

    [Fact]
    public void Render_UnavailableAssetsCullPlaceholdersToVisibleCellsUsing32Maxima()
    {
        SpriteAssetCache cache = SpriteAssetCache.CreateUnavailable();
        MapRenderer renderer = new(cache);
        MapDocument document = MapDocument.Create(4, 4);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        document.SetLayer(3, 3, 0, new MapTileLayer(1, 1));

        RecordingMapDrawSink sink = new();
        renderer.Render(Request(document, Viewport(32, 32)), sink);

        Assert.Equal(1, sink.CallCount);
        PlaceholderDrawOperation operation = (PlaceholderDrawOperation)sink.Calls[0];
        Assert.Equal(SpriteResolutionStatus.AssetsUnavailable, operation.Reason);
        Assert.Equal(new MapTileCoordinate(0, 0), operation.Tile);
        Assert.Equal(new RenderRect(0, 0, 32, 32), operation.DestinationRect);
    }

    [Fact]
    public void Render_DisposedAssetsThrowsBeforeSinkCalls()
    {
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(loader, ManifestJson((1, 1, 0, 0, 32, 32)));
        MapRenderer renderer = new(cache);
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));

        cache.Dispose();

        RecordingMapDrawSink sink = new();
        Assert.Throws<ObjectDisposedException>(() => renderer.Render(Request(document, Viewport(32, 32)), sink));
        Assert.Equal(0, sink.CallCount);
        Assert.Equal(0, loader.CallCount);
    }

    [Fact]
    public void Render_SinkFailurePropagatesAndNextRenderCanRetry()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        byte[] before = MapCodec.Encode(document);

        RecordingMapDrawSink sink = new();
        sink.FailOnCall = 0;
        Assert.Throws<InvalidOperationException>(() => renderer.Render(Request(document, Viewport(32, 32)), sink));
        Assert.Equal(0, sink.CallCount);

        sink.FailOnCall = -1;
        renderer.Render(Request(document, Viewport(32, 32)), sink);
        Assert.Equal(1, sink.CallCount);
        SpriteDrawOperation operation = (SpriteDrawOperation)sink.Calls[0];
        Assert.Equal(new SpriteReference(1, 1), operation.Reference);
        Assert.Equal(before, MapCodec.Encode(document));
    }

    private static SpriteAssetCache CreateCache(FakeSpriteSheetLoader loader, string manifestJson)
        => new(AssetDirectory, SpriteManifest.Parse(manifestJson), loader);

    private static string ManifestJson(params (int Sheet, int Graphic, int X, int Y, int Width, int Height)[] frames)
    {
        string sheets = string.Join(",", frames
            .GroupBy(frame => frame.Sheet)
            .Select(group =>
            {
                string groupFrames = string.Join(",", group
                    .Select(frame => $"\"{frame.Graphic}\":[{frame.X},{frame.Y},{frame.Width},{frame.Height}]"));
                return $"\"{group.Key}\":{{{groupFrames}}}";
            }));
        return $"{{\"tileSize\":32,\"sheets\":{{{sheets}}}}}";
    }

    private static ViewportTransform Viewport(double width, double height, double originX = 0.0, double originY = 0.0, MapZoom zoom = MapZoom.Percent100)
        => new(new RenderSize(width, height), new RenderPoint(originX, originY), zoom);

    private static MapRenderRequest Request(MapDocument document, ViewportTransform viewport, MapRenderOptions? options = null)
        => new(document, viewport, options ?? MapRenderOptions.Default);

    private static MapRenderOptions GridOptions()
        => new(MapLayerVisibility.All, true, false, null, null);

    private static (RenderPoint Start, RenderPoint End)[] AsLines(System.Collections.Generic.IReadOnlyList<object> calls)
        => calls.OfType<GridLineDrawOperation>().Select(line => (line.Start, line.End)).ToArray();

    private static (RenderPoint Start, RenderPoint End)[] SortLines(params (RenderPoint Start, RenderPoint End)[] lines)
        => lines
            .OrderBy(line => line.Start.X)
            .ThenBy(line => line.Start.Y)
            .ThenBy(line => line.End.X)
            .ThenBy(line => line.End.Y)
            .ToArray();
}
