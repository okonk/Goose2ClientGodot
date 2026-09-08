using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapEditor.Core;
using MapEditor.GameData.Rows;
using MapEditor.Rendering;
using MapEditor.Rendering.Tests.Fakes;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class NpcPreviewRenderingTests
{
    private const string MapManifestJson = """
        { "tileSize": 32, "sheets": {
          "1": {
            "1": [0, 0, 32, 32], "2": [0, 0, 32, 32], "3": [0, 0, 32, 32],
            "4": [0, 0, 32, 32], "5": [0, 0, 32, 32]
          },
          "1000": {
            "108760": [0, 0, 48, 64],
            "108761": [16, 0, 32, 48],
            "108762": [0, 0, 64, 64],
            "108763": [0, 0, 47, 49],
            "108764": [0, 0, 32, 32],
            "108765": [0, 0, 32, 96]
          },
          "1001": { "1": [0, 0, 32, 32] }
        } }
        """;

    private const string AppearanceManifestJson = """
        { "version": 1, "parts": {
          "Body": {
            "1": { "noEquip": [1000, 108760], "equip": [1000, 108761] },
            "2": { "noEquip": [1000, 108761] },
            "202": { "noEquip": [1000, 108765] }
          },
          "Eyes": { "7": { "noEquip": [1000, 108764] } },
          "Hair": { "2": { "noEquip": [1000, 108763] } },
          "Legs": { "3": { "noEquip": [1000, 108761] } },
          "Feet": { "4": { "noEquip": [1000, 108761] } },
          "Chest": { "5": { "noEquip": [1000, 108761] } },
          "Helm": { "9": { "noEquip": [1000, 108764] } },
          "Hand": { "6": { "noEquip": [1000, 108763] }, "7": { "noEquip": [1000, 108764] } }
        } }
        """;

    private const string FullEquipment = "5,*,9,*,3,*,4,*,6,*,7,100,110,120,130";

    [Fact]
    public void Render_PreviewModeStagesEntitiesBetweenLayersOneAndThreeWithOverlaysAfter()
    {
        using Fixture fixture = new();
        NpcAppearanceGroup group = fixture.Compose(Humanoid(1, NpcEquipmentParser.DefaultEquippedItems), 0, 1, 0);

        MapDocument document = MapDocument.Create(2, 2);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            document.SetLayer(0, 0, layer, new MapTileLayer(1, layer + 1));
        }
        document.SetFlags(0, 0, MapDocument.BlockedFlag);

        MapRenderOptions options = new(
            MapLayerVisibility.All,
            true,
            true,
            null,
            new MapTileCoordinate(0, 0),
            SpawnMarkers: new[] { new GameDataMarkerInput(0, new MapTileCoordinate(1, 0), true, "npc 1") },
            WarpMarkers: new[] { new GameDataMarkerInput(0, new MapTileCoordinate(1, 1), false, "map 20 (5, 6)") },
            PreviewMode: true,
            NpcPreviews: new[] { group });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options), sink);

        object[] calls = sink.Calls.ToArray();
        Assert.Equal(19, calls.Length);
        Assert.Equal(0, ((SpriteDrawOperation)calls[0]).Layer);
        Assert.Equal(new SpriteReference(1, 1), ((SpriteDrawOperation)calls[0]).Reference);
        Assert.Equal(1, ((SpriteDrawOperation)calls[1]).Layer);
        Assert.Equal(new SpriteReference(1, 2), ((SpriteDrawOperation)calls[1]).Reference);
        SpriteDrawOperation layer2 = (SpriteDrawOperation)calls[3];
        Assert.Equal(2, layer2.Layer);
        Assert.Equal(new MapTileCoordinate(0, 0), layer2.Tile);
        Assert.Equal(new SpriteReference(1, 3), layer2.Reference);
        NpcSpawnAnchorDrawOperation anchor = (NpcSpawnAnchorDrawOperation)calls[2];
        Assert.Equal(0, anchor.OccurrenceIndex);
        Assert.Equal(new MapTileCoordinate(1, 0), anchor.Tile);
        Assert.True(anchor.Selected);
        NpcImageDrawOperation[] parts = calls.Skip(4).Take(4).Cast<NpcImageDrawOperation>().ToArray();
        Assert.Equal(
            new[] { NpcPartSlot.Body, NpcPartSlot.Eyes, NpcPartSlot.Legs, NpcPartSlot.Hair },
            parts.Select(part => part.Slot).ToArray());
        Assert.All(parts, part => Assert.Equal(0, part.OccurrenceIndex));
        Assert.Equal(3, ((SpriteDrawOperation)calls[8]).Layer);
        Assert.Equal(new SpriteReference(1, 4), ((SpriteDrawOperation)calls[8]).Reference);
        Assert.Equal(4, ((SpriteDrawOperation)calls[9]).Layer);
        Assert.Equal(new SpriteReference(1, 5), ((SpriteDrawOperation)calls[9]).Reference);
        Assert.Equal(CellOverlayKind.Blocked, ((CellOverlayDrawOperation)calls[10]).Kind);
        Assert.All(calls.Skip(11).Take(6), call => Assert.IsType<GridLineDrawOperation>(call));
        Assert.Equal(GameDataMarkerKind.Warp, ((GameDataMarkerDrawOperation)calls[17]).Kind);
        Assert.Equal(CellOverlayKind.Selected, ((CellOverlayDrawOperation)calls[18]).Kind);
        Assert.Equal(0, calls.Count(call => call is GameDataMarkerDrawOperation { Kind: GameDataMarkerKind.Spawn }));
    }

    [Fact]
    public void Render_EntityStageSortsByBottomCenterYThenXAcrossKinds()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(4, 4);
        document.SetLayer(0, 1, 2, new MapTileLayer(1, 1));
        document.SetLayer(1, 0, 2, new MapTileLayer(1, 2));
        document.SetLayer(2, 2, 2, new MapTileLayer(1, 3));

        NpcAppearanceGroup a = fixture.Compose(Humanoid(2, NpcEquipmentParser.DefaultEquippedItems), 0, 0, 0);
        NpcAppearanceGroup b = fixture.Compose(Humanoid(2, NpcEquipmentParser.DefaultEquippedItems), 1, 1, 1);
        NpcAppearanceGroup c = fixture.Compose(Humanoid(2, NpcEquipmentParser.DefaultEquippedItems), 2, 2, 0);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true, NpcPreviews: new[] { a, b, c });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(128, 128), options), sink);

        (string Kind, int Occurrence, MapTileCoordinate? Tile)[] expected =
        {
            ("npc", 0, null), ("npc", 0, null), ("npc", 0, null),
            ("map", -1, new MapTileCoordinate(1, 0)),
            ("npc", 2, null), ("npc", 2, null), ("npc", 2, null),
            ("map", -1, new MapTileCoordinate(0, 1)),
            ("npc", 1, null), ("npc", 1, null), ("npc", 1, null),
            ("map", -1, new MapTileCoordinate(2, 2))
        };
        Assert.Equal(expected, EntitySequence(sink));
    }

    [Fact]
    public void Render_SameAnchorTiePutsMapObjectBeforeNpcAndDuplicatesFollowOccurrenceIndex()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(2, 2);
        document.SetLayer(0, 0, 2, new MapTileLayer(1, 1));

        NpcAppearanceGroup later = fixture.Compose(Humanoid(2, NpcEquipmentParser.DefaultEquippedItems), 5, 0, 0);
        NpcAppearanceGroup earlier = fixture.Compose(Humanoid(2, NpcEquipmentParser.DefaultEquippedItems), 2, 0, 0);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true, NpcPreviews: new[] { later, earlier });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options), sink);

        Assert.Equal(
            new (string, int, MapTileCoordinate?)[]
            {
                ("map", -1, new MapTileCoordinate(0, 0)),
                ("npc", 2, null), ("npc", 2, null), ("npc", 2, null),
                ("npc", 5, null), ("npc", 5, null), ("npc", 5, null)
            },
            EntitySequence(sink));
    }

    [Fact]
    public void Render_RepeatedRendersEmitIdenticalSequences()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(4, 4);
        document.SetLayer(0, 1, 2, new MapTileLayer(1, 1));
        document.SetLayer(1, 0, 2, new MapTileLayer(1, 2));
        NpcAppearanceGroup a = fixture.Compose(Humanoid(1, FullEquipment), 0, 0, 0);
        NpcAppearanceGroup b = fixture.Compose(Humanoid(2, NpcEquipmentParser.DefaultEquippedItems), 1, 1, 1);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true, NpcPreviews: new[] { a, b });

        RecordingMapDrawSink first = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(128, 128), options), first);
        RecordingMapDrawSink second = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(128, 128), options), second);

        Assert.True(first.Calls.SequenceEqual(second.Calls));
    }

    [Fact]
    public void Render_TallMultiPartNpcEmitsEveryPartContiguouslyBeforeTheNextEntity()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(2, 2);
        document.SetLayer(1, 1, 2, new MapTileLayer(1, 1));

        NpcAppearanceGroup tall = fixture.Compose(Humanoid(1, FullEquipment), 0, 0, 1);
        NpcAppearanceGroup short_ = fixture.Compose(Humanoid(2, NpcEquipmentParser.DefaultEquippedItems), 1, 0, 0);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true, NpcPreviews: new[] { tall, short_ });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options), sink);

        object[] calls = sink.Calls.ToArray();
        Assert.Equal(15, calls.Length);
        NpcSpawnAnchorDrawOperation[] anchors = calls.Take(2).Cast<NpcSpawnAnchorDrawOperation>().ToArray();
        Assert.Equal(new[] { 0, 1 }, anchors.Select(anchor => anchor.OccurrenceIndex).ToArray());
        Assert.All(calls.Skip(2).Take(3), call => Assert.Equal(1, ((NpcImageDrawOperation)call).OccurrenceIndex));
        NpcImageDrawOperation[] tallParts = calls.Skip(5).Take(9).Cast<NpcImageDrawOperation>().ToArray();
        Assert.Equal(
            new[]
            {
                NpcPartSlot.Body, NpcPartSlot.Eyes, NpcPartSlot.Feet, NpcPartSlot.Legs, NpcPartSlot.Chest,
                NpcPartSlot.Hair, NpcPartSlot.Helm, NpcPartSlot.Shield, NpcPartSlot.Weapon
            },
            tallParts.Select(part => part.Slot).ToArray());
        Assert.All(tallParts, part => Assert.Equal(0, part.OccurrenceIndex));
        SpriteDrawOperation layer2 = (SpriteDrawOperation)calls[14];
        Assert.Equal(2, layer2.Layer);
        Assert.Equal(new MapTileCoordinate(1, 1), layer2.Tile);
    }

    [Fact]
    public void Render_SortKeyIgnoresSpriteHeightAndZoom()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(3, 1);
        NpcAppearanceGroup tall64 = fixture.Compose(Humanoid(1, NpcEquipmentParser.DefaultEquippedItems), 0, 0, 0);
        NpcAppearanceGroup standard48 = fixture.Compose(Humanoid(2, NpcEquipmentParser.DefaultEquippedItems), 1, 1, 0);
        NpcAppearanceGroup tall96 = fixture.Compose(Humanoid(202, NpcEquipmentParser.DefaultEquippedItems), 2, 2, 0);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true, NpcPreviews: new[] { tall64, standard48, tall96 });

        RecordingMapDrawSink at100 = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(96, 32), options), at100);
        RecordingMapDrawSink at200 = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(192, 64, zoom: MapZoom.Percent200), options), at200);

        (string Kind, int Occurrence, MapTileCoordinate? Tile)[] expected =
        {
            ("npc", 0, null), ("npc", 0, null), ("npc", 0, null), ("npc", 0, null),
            ("npc", 1, null), ("npc", 1, null), ("npc", 1, null),
            ("npc", 2, null)
        };
        Assert.Equal(expected, EntitySequence(at100));
        Assert.Equal(expected, EntitySequence(at200));

        NpcImageDrawOperation body100 = at100.Calls.OfType<NpcImageDrawOperation>().First(part => part.OccurrenceIndex == 0 && part.Slot == NpcPartSlot.Body);
        Assert.Equal(new RenderRect(-8, -24, 48, 64), body100.DestinationRect);
        Assert.Equal(SpriteSampling.NearestNeighbor, body100.Sampling);
        Assert.Equal(new RgbaValue(10, 20, 30, 40), body100.Tint);
        NpcImageDrawOperation body200 = at200.Calls.OfType<NpcImageDrawOperation>().First(part => part.OccurrenceIndex == 0 && part.Slot == NpcPartSlot.Body);
        Assert.Equal(new RenderRect(-16, -48, 96, 128), body200.DestinationRect);
    }

    [Fact]
    public void Render_OffscreenGroupsEmitNothing()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(2, 2);
        NpcAppearanceGroup offscreen = fixture.Compose(Humanoid(100, NpcEquipmentParser.DefaultEquippedItems), 0, 1, 1);
        NpcAppearanceGroup outOfMap = fixture.Compose(Humanoid(100, NpcEquipmentParser.DefaultEquippedItems), 1, 5, 5);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true, NpcPreviews: new[] { offscreen, outOfMap });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(32, 32), options), sink);

        Assert.Equal(0, sink.CallCount);
    }

    [Fact]
    public void Render_PartiallyVisibleTallPartEmitsWithoutAnchor()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(1, 2);
        NpcAppearanceGroup group = fixture.Compose(Humanoid(202, NpcEquipmentParser.DefaultEquippedItems), 0, 0, 1);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true, NpcPreviews: new[] { group });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(32, 32), options), sink);

        NpcImageDrawOperation[] parts = sink.Calls.OfType<NpcImageDrawOperation>().ToArray();
        Assert.Single(parts);
        Assert.Equal(NpcPartSlot.Body, parts[0].Slot);
        Assert.Equal(new RenderRect(0, -8, 32, 96), parts[0].DestinationRect);
        Assert.Equal(new SpriteSourceRect(0, 0, 32, 96), parts[0].SourceRect);
        Assert.Equal(0, sink.Calls.Count(call => call is NpcSpawnAnchorDrawOperation));
        Assert.Equal(0, sink.Calls.Count(call => call is NpcPartPlaceholderDrawOperation));
    }

    [Fact]
    public void Render_WorkLimitCountsEntityCandidateReadsBeforeAllocation()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(400, 400);
        ViewportTransform viewport = Viewport(12800, 12800);
        MapRenderOptions atLimit = new(MapLayerVisibility.All, false, false, null, null);

        RecordingMapDrawSink atLimitSink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, viewport, atLimit), atLimitSink);
        Assert.Equal(0, atLimitSink.CallCount);

        NpcAppearanceGroup group = fixture.Compose(Humanoid(1, FullEquipment), 0, 0, 0);
        MapRenderOptions over = atLimit with
        {
            PreviewMode = true,
            NpcPreviews = new[] { group }
        };
        RecordingMapDrawSink overSink = new();
        MapRenderWorkLimitException exception = Assert.Throws<MapRenderWorkLimitException>(
            () => fixture.Renderer.Render(new MapRenderRequest(document, viewport, over), overSink));
        Assert.Equal(800_009L, exception.RequestedTileReads);
        Assert.Equal(MapRenderer.MaximumTileReadsPerRender, exception.MaximumTileReads);
        Assert.Equal(0, overSink.CallCount);

        NpcAppearanceGroup outOfMap = fixture.Compose(Humanoid(1, FullEquipment), 0, 9999, 9999);
        MapRenderOptions notCounted = atLimit with
        {
            PreviewMode = true,
            NpcPreviews = new[] { outOfMap }
        };
        RecordingMapDrawSink notCountedSink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, viewport, notCounted), notCountedSink);
        Assert.Equal(0, notCountedSink.CallCount);
    }

    [Fact]
    public void Render_NormalModeEmitsCompactMarkerAndNoAppearanceOperations()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(2, 1);
        document.SetLayer(0, 0, 2, new MapTileLayer(1, 1));
        NpcAppearanceGroup group = fixture.Compose(Humanoid(1, NpcEquipmentParser.DefaultEquippedItems), 3, 1, 0);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            SpawnMarkers: new[] { new GameDataMarkerInput(3, new MapTileCoordinate(1, 0), true, "npc 4") },
            PreviewMode: false,
            NpcPreviews: new[] { group });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 32), options), sink);

        Assert.Equal(2, sink.CallCount);
        SpriteDrawOperation layer2 = (SpriteDrawOperation)sink.Calls[0];
        Assert.Equal(2, layer2.Layer);
        GameDataMarkerDrawOperation marker = (GameDataMarkerDrawOperation)sink.Calls[1];
        Assert.Equal(GameDataMarkerKind.Spawn, marker.Kind);
        Assert.Equal(3, marker.OccurrenceIndex);
        Assert.True(marker.Selected);
        Assert.Equal(0, sink.Calls.Count(call => call is NpcImageDrawOperation or NpcPartPlaceholderDrawOperation or NpcSpawnAnchorDrawOperation));
    }

    [Fact]
    public void Render_PreviewModeSuppressesCompactSpawnMarkerKeepsWarpAndEmitsAnchor()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(2, 1);
        document.SetLayer(0, 0, 2, new MapTileLayer(1, 1));
        NpcAppearanceGroup group = fixture.Compose(Humanoid(1, NpcEquipmentParser.DefaultEquippedItems), 3, 1, 0);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            SpawnMarkers: new[] { new GameDataMarkerInput(3, new MapTileCoordinate(1, 0), true, "npc 4") },
            WarpMarkers: new[] { new GameDataMarkerInput(0, new MapTileCoordinate(0, 0), false, "map 20 (5, 6)") },
            PreviewMode: true,
            NpcPreviews: new[] { group });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 32), options), sink);

        Assert.Equal(4, sink.Calls.Count(call => call is NpcImageDrawOperation));
        Assert.Equal(0, sink.Calls.Count(call => call is GameDataMarkerDrawOperation { Kind: GameDataMarkerKind.Spawn }));
        GameDataMarkerDrawOperation warp = sink.Calls.OfType<GameDataMarkerDrawOperation>().Single();
        Assert.Equal(GameDataMarkerKind.Warp, warp.Kind);
        NpcSpawnAnchorDrawOperation anchor = sink.Calls.OfType<NpcSpawnAnchorDrawOperation>().Single();
        Assert.Equal(3, anchor.OccurrenceIndex);
        Assert.Equal(new MapTileCoordinate(1, 0), anchor.Tile);
        Assert.Equal(new RenderRect(32, 0, 32, 32), anchor.DestinationRect);
        Assert.True(anchor.Selected);
        Assert.Null(anchor.Diagnostic);
    }

    [Fact]
    public void Render_PreviewModeEmitsPlaceholdersAndAnAnchorForUnknownMalformedAndFailedSpawns()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(2, 2);
        NpcAppearanceGroup unknown = fixture.Compose(Humanoid(999999, NpcEquipmentParser.DefaultEquippedItems), 0, 0, 0);
        NpcAppearanceGroup malformed = fixture.Compose(Humanoid(2, "0,*,garbage"), 1, 1, 0);
        NpcAppearanceGroup failed = fixture.Compose(Humanoid(0, NpcEquipmentParser.DefaultEquippedItems), 2, 0, 1);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true,
            NpcPreviews: new[] { unknown, malformed, failed });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options), sink);

        NpcSpawnAnchorDrawOperation[] anchors = sink.Calls.OfType<NpcSpawnAnchorDrawOperation>().ToArray();
        Assert.Equal(new[] { 0, 1, 2 }, anchors.Select(anchor => anchor.OccurrenceIndex).ToArray());
        Assert.Equal(new MapTileCoordinate(1, 0), anchors[1].Tile);
        Assert.False(string.IsNullOrEmpty(anchors[1].Diagnostic));
        Assert.Null(anchors[0].Diagnostic);
        Assert.Null(anchors[2].Diagnostic);

        NpcPartPlaceholderDrawOperation[] placeholders = sink.Calls.OfType<NpcPartPlaceholderDrawOperation>().ToArray();
        Assert.Equal(2, placeholders.Length);
        Assert.Equal((0, NpcPartSlot.Body, SpriteResolutionStatus.UnknownGraphic),
            (placeholders[0].OccurrenceIndex, placeholders[0].Slot, placeholders[0].Reason));
        Assert.Equal((2, NpcPartSlot.Body, SpriteResolutionStatus.Empty),
            (placeholders[1].OccurrenceIndex, placeholders[1].Slot, placeholders[1].Reason));
        Assert.Equal(new RenderRect(0, 0, 32, 32), placeholders[0].DestinationRect);
        Assert.Equal(new RenderRect(0, 32, 32, 32), placeholders[1].DestinationRect);
        Assert.All(placeholders, placeholder =>
        {
            Assert.Equal(new RenderColor(0xFF, 0x00, 0xFF, 0xCC), placeholder.FillColor);
            Assert.Equal(new RenderColor(0xFF, 0xFF, 0x00, 0xFF), placeholder.StrokeColor);
            Assert.False(string.IsNullOrEmpty(placeholder.Diagnostic));
        });

        Assert.Equal(5, sink.Calls.Count(call => call is NpcImageDrawOperation));
    }

    [Fact]
    public void Render_PreviewModeMixedStateKeepsCompactMarkerOnlyForSpawnsWithoutAGroup()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(2, 1);
        NpcAppearanceGroup grouped = fixture.Compose(Humanoid(2, NpcEquipmentParser.DefaultEquippedItems), 1, 1, 0);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            SpawnMarkers: new[]
            {
                new GameDataMarkerInput(0, new MapTileCoordinate(0, 0), false, "npc 1"),
                new GameDataMarkerInput(1, new MapTileCoordinate(1, 0), true, "npc 2")
            },
            PreviewMode: true,
            NpcPreviews: new[] { grouped });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 32), options), sink);

        object[] calls = sink.Calls.ToArray();
        Assert.Equal(5, calls.Length);
        NpcSpawnAnchorDrawOperation anchor = (NpcSpawnAnchorDrawOperation)calls[0];
        Assert.Equal(1, anchor.OccurrenceIndex);
        Assert.Equal(new MapTileCoordinate(1, 0), anchor.Tile);
        Assert.True(anchor.Selected);
        NpcImageDrawOperation[] parts = calls.Skip(1).Take(3).Cast<NpcImageDrawOperation>().ToArray();
        Assert.Equal(new[] { NpcPartSlot.Body, NpcPartSlot.Eyes, NpcPartSlot.Hair }, parts.Select(part => part.Slot).ToArray());
        Assert.All(parts, part => Assert.Equal(1, part.OccurrenceIndex));
        GameDataMarkerDrawOperation marker = (GameDataMarkerDrawOperation)calls[4];
        Assert.Equal(GameDataMarkerKind.Spawn, marker.Kind);
        Assert.Equal(0, marker.OccurrenceIndex);
        Assert.Equal(new MapTileCoordinate(0, 0), marker.Tile);
        Assert.False(marker.Selected);
        Assert.Equal(1, calls.Count(call => call is GameDataMarkerDrawOperation));
        Assert.Equal(1, calls.Count(call => call is NpcSpawnAnchorDrawOperation));
    }

    [Fact]
    public void Render_PreviewModeEmitsAnchorForFullyTransparentReadyArtWithoutPlaceholders()
    {
        using Fixture fixture = new();
        MapDocument document = MapDocument.Create(1, 1);
        NpcAppearance transparent = new(
            1, "Goose", 3, 2, new RgbaValue(0, 0, 0, 0), 7, 2, new RgbaValue(0, 0, 0, 0),
            NpcEquipmentParser.DefaultEquippedItems);
        NpcAppearanceGroup group = fixture.Compose(transparent, 0, 0, 0);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true,
            NpcPreviews: new[] { group });

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(32, 32), options), sink);

        NpcImageDrawOperation[] parts = sink.Calls.OfType<NpcImageDrawOperation>().ToArray();
        Assert.Equal(3, parts.Length);
        Assert.All(parts, part => Assert.Equal(new RgbaValue(0, 0, 0, 0), part.Tint));
        Assert.Equal(0, sink.Calls.Count(call => call is NpcPartPlaceholderDrawOperation));
        NpcSpawnAnchorDrawOperation anchor = sink.Calls.OfType<NpcSpawnAnchorDrawOperation>().Single();
        Assert.Equal(0, anchor.OccurrenceIndex);
        Assert.Equal(new MapTileCoordinate(0, 0), anchor.Tile);
        Assert.Equal(new RenderRect(0, 0, 32, 32), anchor.DestinationRect);
    }

    [Fact]
    public void Render_EmptyPreviewInputsPreserveLegacyCallSequence()
    {
        using Fixture fixture = new();
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

        RecordingMapDrawSink baseline = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 32), options), baseline);

        Assert.Equal(19, baseline.CallCount);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            SpriteDrawOperation first = (SpriteDrawOperation)baseline.Calls[layer * 2];
            SpriteDrawOperation second = (SpriteDrawOperation)baseline.Calls[layer * 2 + 1];
            Assert.Equal(layer, first.Layer);
            Assert.Equal(new MapTileCoordinate(0, 0), first.Tile);
            Assert.Equal(layer, second.Layer);
            Assert.Equal(new MapTileCoordinate(1, 0), second.Tile);
        }
        Assert.All(baseline.Calls.Skip(10).Take(2), call =>
            Assert.Equal(CellOverlayKind.Blocked, ((CellOverlayDrawOperation)call).Kind));
        Assert.All(baseline.Calls.Skip(12).Take(5), call => Assert.IsType<GridLineDrawOperation>(call));
        Assert.Equal(CellOverlayKind.Selected, ((CellOverlayDrawOperation)baseline.Calls[17]).Kind);
        Assert.Equal(CellOverlayKind.Hovered, ((CellOverlayDrawOperation)baseline.Calls[18]).Kind);

        MapRenderOptions emptyPreview = options with
        {
            PreviewMode = true,
            NpcPreviews = Array.Empty<NpcAppearanceGroup>()
        };
        RecordingMapDrawSink preview = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 32), emptyPreview), preview);

        Assert.True(baseline.Calls.SequenceEqual(preview.Calls));
    }

    [Fact]
    public void Render_PreviewNamesRenderAboveTheHeadOnlyWhenShowNamesIsEnabled()
    {
        using Fixture fixture = new();
        NpcAppearanceGroup group = fixture.Compose(Humanoid(1, NpcEquipmentParser.DefaultEquippedItems), 0, 1, 0) with { Name = "Goose" };
        MapDocument document = MapDocument.Create(2, 2);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true, NpcPreviews: new[] { group }, ShowNames: true);

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options), sink);

        NpcNameDrawOperation name = sink.Calls.OfType<NpcNameDrawOperation>().Single();
        Assert.Equal(0, name.OccurrenceIndex);
        Assert.Equal("Goose", name.Name);
        Assert.Equal(new RenderPoint(48, -31.6), name.Center);
        Assert.Equal(11.2, name.FontSize);

        object[] calls = sink.Calls.ToArray();
        int nameIndex = Array.FindIndex(calls, op => op is NpcNameDrawOperation);
        int lastPart = -1;
        for (int i = 0; i < calls.Length; i++)
        {
            if (calls[i] is NpcImageDrawOperation)
            {
                lastPart = i;
            }
        }

        Assert.True(lastPart >= 0 && nameIndex > lastPart);

        RecordingMapDrawSink hiddenSink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options with { ShowNames = false }), hiddenSink);
        Assert.Equal(0, hiddenSink.Calls.Count(call => call is NpcNameDrawOperation));
    }

    [Fact]
    public void Render_PreviewNameForAnUnknownNpcSitsAboveTheCell()
    {
        using Fixture fixture = new();
        NpcAppearance unknown = new(999999, "Nobody", 3, 999999, default, 0, 0, default, NpcEquipmentParser.DefaultEquippedItems);
        NpcAppearanceGroup group = fixture.Compose(unknown, 0, 0, 0) with { Name = "999999" };
        MapDocument document = MapDocument.Create(2, 2);
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true, NpcPreviews: new[] { group }, ShowNames: true);

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options), sink);

        NpcNameDrawOperation name = sink.Calls.OfType<NpcNameDrawOperation>().Single();
        Assert.Equal(new RenderPoint(16, -7.6), name.Center);
    }

    [Fact]
    public void Render_PreviewNamesRenderAboveTheUpperTileLayers()
    {
        using Fixture fixture = new();
        NpcAppearanceGroup group = fixture.Compose(Humanoid(1, NpcEquipmentParser.DefaultEquippedItems), 0, 1, 0) with { Name = "Goose" };
        MapDocument document = MapDocument.Create(2, 2);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            document.SetLayer(0, 0, layer, new MapTileLayer(1, layer + 1));
        }
        MapRenderOptions options = new(
            MapLayerVisibility.All, false, false, null, null,
            PreviewMode: true, NpcPreviews: new[] { group }, ShowNames: true);

        RecordingMapDrawSink sink = new();
        fixture.Renderer.Render(new MapRenderRequest(document, Viewport(64, 64), options), sink);

        object[] calls = sink.Calls.ToArray();
        int nameIndex = Array.FindIndex(calls, op => op is NpcNameDrawOperation);
        int lastUpperLayer = Array.FindLastIndex(calls, op => op is SpriteDrawOperation { Layer: >= 3 });
        Assert.True(nameIndex >= 0 && lastUpperLayer >= 0 && nameIndex > lastUpperLayer);
    }

    private static NpcAppearance Humanoid(int bodyId, string equipped, int bodyState = 3)
        => new(1, "Goose", bodyState, bodyId, new RgbaValue(10, 20, 30, 40), 7, 2, new RgbaValue(50, 60, 70, 80), equipped);

    private static (string Kind, int Occurrence, MapTileCoordinate? Tile)[] EntitySequence(RecordingMapDrawSink sink)
    {
        List<(string, int, MapTileCoordinate?)> sequence = new();
        foreach (object call in sink.Calls)
        {
            if (call is NpcImageDrawOperation image)
            {
                sequence.Add(("npc", image.OccurrenceIndex, null));
            }
            else if (call is NpcPartPlaceholderDrawOperation placeholder)
            {
                sequence.Add(("npc", placeholder.OccurrenceIndex, null));
            }
            else if (call is SpriteDrawOperation { Layer: 2 } sprite)
            {
                sequence.Add(("map", -1, sprite.Tile));
            }
            else if (call is NpcSpawnAnchorDrawOperation)
            {
                continue;
            }
            else
            {
                break;
            }
        }

        return sequence.ToArray();
    }

    private static ViewportTransform Viewport(double width, double height, double originX = 0.0, double originY = 0.0, MapZoom zoom = MapZoom.Percent100)
        => new(new RenderSize(width, height), new RenderPoint(originX, originY), zoom);

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            AssetRoot = Path.Combine(Path.GetTempPath(), "npc-preview-rendering-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(AssetRoot);
            Cache = new SpriteAssetCache(
                AssetRoot,
                SpriteManifest.Parse(MapManifestJson),
                new FakeSpriteSheetLoader(_ => SpriteSheetLoadResult.Success(new FakeSpriteSheetImage(128, 128))));
            Composer = new NpcAppearanceComposer(new AppearanceAssetCatalog(AppearanceManifest.Parse(AppearanceManifestJson), Cache));
        }

        public string AssetRoot { get; }

        public SpriteAssetCache Cache { get; }

        public NpcAppearanceComposer Composer { get; }

        public MapRenderer Renderer => new(Cache);

        public NpcAppearanceGroup Compose(NpcAppearance appearance, int occurrenceIndex, int tileX, int tileY)
            => Composer.Compose(appearance, occurrenceIndex, tileX, tileY);

        public void Dispose()
        {
            Cache.Dispose();
            Directory.Delete(AssetRoot, recursive: true);
        }
    }
}
