using System.Linq;
using MapEditor.Core;
using MapEditor.Rendering;
using MapEditor.Rendering.Tests.Fakes;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class GameDataMarkerRenderingTests
{
    private const string AssetDirectory = "test-assets";

    [Fact]
    public void Render_MarkersDrawAfterSpritesAndOverlaysButBeforeTheSelectionAffordance()
    {
        string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
        MapDocument document = MapDocument.Create(2, 2);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        document.SetFlags(1, 0, MapDocument.BlockedFlag);

        MapRenderOptions options = new(
            MapLayerVisibility.All,
            true,
            true,
            null,
            new MapTileCoordinate(0, 0),
            SpawnMarkers: new[]
            {
                new GameDataMarkerInput(0, new MapTileCoordinate(0, 1), true, "npc 1"),
                new GameDataMarkerInput(1, new MapTileCoordinate(1, 1), false, "npc 2")
            },
            WarpMarkers: new[] { new GameDataMarkerInput(0, new MapTileCoordinate(1, 0), false, "map 20 (7, 8)") });

        RecordingMapDrawSink sink = new();
        renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options), sink);

        object[] calls = sink.Calls.ToArray();
        int firstMarker = IndexOfFirst(calls, op => op is GameDataMarkerDrawOperation);
        int lastMarker = IndexOfLast(calls, op => op is GameDataMarkerDrawOperation);
        Assert.True(firstMarker >= 0);
        Assert.True(lastMarker >= firstMarker);

        Assert.Equal(3, lastMarker - firstMarker + 1);
        Assert.All(calls.Take(firstMarker), op => Assert.False(op is GameDataMarkerDrawOperation));
        Assert.IsType<SpriteDrawOperation>(calls[0]);
        Assert.Contains(calls.Take(firstMarker), op => op is CellOverlayDrawOperation { Kind: CellOverlayKind.Blocked });
        Assert.Contains(calls.Take(firstMarker), op => op is GridLineDrawOperation);

        GameDataMarkerDrawOperation[] markers = calls.Skip(firstMarker).Take(3).Cast<GameDataMarkerDrawOperation>().ToArray();
        Assert.Equal(GameDataMarkerKind.Spawn, markers[0].Kind);
        Assert.Equal(0, markers[0].OccurrenceIndex);
        Assert.Equal(new MapTileCoordinate(0, 1), markers[0].Tile);
        Assert.True(markers[0].Selected);
        Assert.Equal("npc 1", markers[0].Diagnostic);
        Assert.Equal(GameDataMarkerKind.Spawn, markers[1].Kind);
        Assert.Equal(1, markers[1].OccurrenceIndex);
        Assert.Equal(new MapTileCoordinate(1, 1), markers[1].Tile);
        Assert.False(markers[1].Selected);
        Assert.Equal(GameDataMarkerKind.Warp, markers[2].Kind);
        Assert.Equal(0, markers[2].OccurrenceIndex);
        Assert.Equal(new MapTileCoordinate(1, 0), markers[2].Tile);
        Assert.Equal("map 20 (7, 8)", markers[2].Diagnostic);

        Assert.Contains(calls.Skip(lastMarker + 1), op => op is CellOverlayDrawOperation { Kind: CellOverlayKind.Selected });
        Assert.Equal(1, calls.Count(op => op is SpriteDrawOperation));
        Assert.Equal(0, calls.Count(op => op is PlaceholderDrawOperation));
    }

    [Fact]
    public void Render_CullsMarkersOutsideTheViewportAndOutOfBounds()
    {
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), ManifestJson((1, 1, 0, 0, 32, 32))));
        MapDocument document = MapDocument.Create(4, 4);
        MapRenderOptions options = new(
            MapLayerVisibility.All,
            false,
            false,
            null,
            null,
            SpawnMarkers: new[]
            {
                new GameDataMarkerInput(0, new MapTileCoordinate(1, 0), false, "npc 1"),
                new GameDataMarkerInput(1, new MapTileCoordinate(0, 0), false, "npc 2"),
                new GameDataMarkerInput(2, new MapTileCoordinate(5, 0), false, "npc 3"),
                new GameDataMarkerInput(3, new MapTileCoordinate(1, 9), false, "npc 4"),
                new GameDataMarkerInput(4, new MapTileCoordinate(-1, 0), false, "npc 5")
            });

        RecordingMapDrawSink sink = new();
        renderer.Render(new MapRenderRequest(document, Viewport(32, 32, 32, 0), options), sink);

        GameDataMarkerDrawOperation[] markers = sink.Calls.OfType<GameDataMarkerDrawOperation>().ToArray();
        Assert.Single(markers);
        Assert.Equal(0, markers[0].OccurrenceIndex);
        Assert.Equal(new MapTileCoordinate(1, 0), markers[0].Tile);
    }

    [Fact]
    public void Render_EmitsOneMarkerPerOccurrenceAndIdentifiesTheSelectedOne()
    {
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), ManifestJson((1, 1, 0, 0, 32, 32))));
        MapDocument document = MapDocument.Create(2, 2);
        MapRenderOptions options = new(
            MapLayerVisibility.All,
            false,
            false,
            null,
            null,
            WarpMarkers: new[]
            {
                new GameDataMarkerInput(0, new MapTileCoordinate(1, 1), false, "map 20 (5, 6)"),
                new GameDataMarkerInput(1, new MapTileCoordinate(1, 1), true, "map 30 (7, 8)")
            });

        RecordingMapDrawSink sink = new();
        renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options), sink);

        GameDataMarkerDrawOperation[] markers = sink.Calls.OfType<GameDataMarkerDrawOperation>().ToArray();
        Assert.Equal(2, markers.Length);
        Assert.All(markers, marker => Assert.Equal(new MapTileCoordinate(1, 1), marker.Tile));
        Assert.Equal(0, markers[0].OccurrenceIndex);
        Assert.False(markers[0].Selected);
        Assert.Equal(1, markers[1].OccurrenceIndex);
        Assert.True(markers[1].Selected);
    }

    [Fact]
    public void Render_EmitsMarkersWhenPreviewModeIsOn()
    {
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), ManifestJson((1, 1, 0, 0, 32, 32))));
        MapDocument document = MapDocument.Create(2, 2);
        MapRenderOptions options = new(
            MapLayerVisibility.All,
            false,
            false,
            null,
            null,
            SpawnMarkers: new[] { new GameDataMarkerInput(0, new MapTileCoordinate(0, 0), true, "npc 1") },
            WarpMarkers: new[] { new GameDataMarkerInput(0, new MapTileCoordinate(1, 1), false, "map 20 (5, 6)") },
            PreviewMode: true);

        RecordingMapDrawSink sink = new();
        renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options), sink);

        GameDataMarkerDrawOperation[] markers = sink.Calls.OfType<GameDataMarkerDrawOperation>().ToArray();
        Assert.Equal(2, markers.Length);
        Assert.Equal(GameDataMarkerKind.Spawn, markers[0].Kind);
        Assert.Equal(GameDataMarkerKind.Warp, markers[1].Kind);
        Assert.Equal(0, sink.Calls.Count(op => op is SpriteDrawOperation or PlaceholderDrawOperation));
    }

    [Fact]
    public void Render_EmitsNoMarkersWithoutInputs()
    {
        MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), ManifestJson((1, 1, 0, 0, 32, 32))));
        MapDocument document = MapDocument.Create(2, 2);

        RecordingMapDrawSink sink = new();
        renderer.Render(new MapRenderRequest(document, Viewport(64, 64), MapRenderOptions.Default), sink);

        Assert.Equal(0, sink.Calls.Count(op => op is GameDataMarkerDrawOperation));
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

    private static ViewportTransform Viewport(double width, double height, double originX = 0.0, double originY = 0.0)
        => new(new RenderSize(width, height), new RenderPoint(originX, originY), MapZoom.Percent100);

    private static int IndexOfFirst(object[] calls, System.Predicate<object> match)
    {
        for (int i = 0; i < calls.Length; i++)
        {
            if (match(calls[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfLast(object[] calls, System.Predicate<object> match)
    {
        for (int i = calls.Length - 1; i >= 0; i--)
        {
            if (match(calls[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
