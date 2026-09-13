using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MapEditor.App.Terrain;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class TerrainEditorSessionTests
{
    private static readonly Guid GrassId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DirtId = new("00000000-0000-0000-0000-000000000002");
    private static readonly TerrainGraphicReference GrassGraphic = new(1, 10);
    private static readonly TerrainGraphicReference DirtGraphic = new(1, 20);
    private static readonly TerrainGraphicReference PeerGraphic = new(2, 40);
    private static readonly TerrainGraphicReference GrassOnlyPeerGraphic = new(2, 41);

    private static SpriteManifest CreateManifest()
        => SpriteManifest.Parse(
            "{ \"tileSize\": 32, \"sheets\": { \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] }, \"2\": { \"40\": [0, 0, 32, 32], \"41\": [32, 0, 32, 32] } } }");

    private static TerrainCatalog CreateBaseline()
        => new(
            new List<TerrainDefinition>
            {
                new(GrassId, "Grass", null),
                new(DirtId, "Dirt", null)
            },
            new List<TerrainGraphicDefinition>
            {
                new(GrassGraphic, new TerrainPattern(Center: GrassId, North: DirtId)),
                new(DirtGraphic, new TerrainPattern(Center: DirtId, South: GrassId))
            });

    private static TerrainEditorSession CreateSession(out TerrainCatalog baseline)
    {
        baseline = CreateBaseline();
        return new TerrainEditorSession(baseline, CreateManifest());
    }

    private static byte[] Serialize(TerrainCatalog catalog)
        => Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(catalog));

    private static string NameOf(TerrainEditorSession session, Guid id)
        => session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == id).Name;

    [Fact]
    public void Constructor_AndMutations_AreDeeplyIsolated()
    {
        var session = CreateSession(out var baseline);
        var before = Serialize(baseline);
        Assert.Equal(baseline, session.CurrentCatalog);

        var id = session.AddTerrain();
        Assert.True(session.RenameTerrain(id, "Renamed"));
        Assert.True(session.SetColorOverride(id, new TerrainColor(9, 8, 7)));
        session.BeginRegionStroke(GrassId);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.East));
        Assert.True(session.CompleteRegionStroke());
        Assert.True(session.DeleteTerrain(id));

        Assert.Equal(before, Serialize(baseline));
        Assert.Equal(2, baseline.Terrains.Count);
        Assert.Equal(2, baseline.Graphics.Count);
    }

    [Fact]
    public void AddTerrain_GeneratesNonEmptyGuids_WithUniqueDefaultNames()
    {
        var session = CreateSession(out _);

        var first = session.AddTerrain();
        var second = session.AddTerrain();
        var third = session.AddTerrain();

        Assert.NotEqual(Guid.Empty, first);
        Assert.NotEqual(first, second);
        Assert.NotEqual(second, third);
        Assert.Equal(
            new[] { "Grass", "Dirt", "Terrain", "Terrain 2", "Terrain 3" },
            session.CurrentCatalog.Terrains.Select(terrain => terrain.Name));
    }

    [Fact]
    public void AddTerrain_SkipsCaseInsensitivelyTakenDefaultNames()
    {
        var session = CreateSession(out _);
        var first = session.AddTerrain();
        var second = session.AddTerrain();
        Assert.True(session.RenameTerrain(first, "terrain"));
        Assert.True(session.RenameTerrain(second, "terrain 2"));

        var next = session.AddTerrain();

        Assert.Equal("Terrain 3", NameOf(session, next));
    }

    [Fact]
    public void RenameTerrain_SameName_IsNoOp()
    {
        var session = CreateSession(out _);
        var events = new List<TerrainEditorChangeKind>();
        session.Changed += events.Add;

        Assert.True(session.RenameTerrain(GrassId, "Grass"));
        Assert.False(session.CanUndo);
        Assert.Empty(events);
        Assert.False(session.RenameTerrain(Guid.NewGuid(), "X"));
        Assert.Empty(events);
    }

    [Fact]
    public void SetColorOverride_SameColor_IsNoOp()
    {
        var session = CreateSession(out _);
        var events = new List<TerrainEditorChangeKind>();
        session.Changed += events.Add;
        var color = new TerrainColor(1, 2, 3);

        Assert.True(session.SetColorOverride(GrassId, color));
        Assert.Single(events);
        Assert.True(session.SetColorOverride(GrassId, color));
        Assert.Single(events);
        Assert.False(session.SetColorOverride(Guid.NewGuid(), color));
        Assert.Single(events);
    }

    [Fact]
    public void RenameTerrain_PreservesDerivedDisplayColor()
    {
        var session = CreateSession(out _);
        var before = session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == GrassId).DisplayColor;

        Assert.True(session.RenameTerrain(GrassId, "Renamed"));

        var after = session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == GrassId).DisplayColor;
        Assert.Equal(before, after);

        var overrideColor = new TerrainColor(5, 6, 7);
        Assert.True(session.SetColorOverride(GrassId, overrideColor));
        Assert.True(session.RenameTerrain(GrassId, "Renamed Again"));
        var overridden = session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == GrassId).DisplayColor;
        Assert.Equal(overrideColor, overridden);
    }

    [Fact]
    public void DeleteTerrain_Undo_RestoresGraphicsAndAllPeers()
    {
        var baseline = new TerrainCatalog(
            new List<TerrainDefinition>
            {
                new(GrassId, "Grass", null),
                new(DirtId, "Dirt", null)
            },
            new List<TerrainGraphicDefinition>
            {
                new(GrassGraphic, new TerrainPattern(Center: GrassId, North: DirtId)),
                new(DirtGraphic, new TerrainPattern(Center: DirtId, South: GrassId)),
                new(PeerGraphic, new TerrainPattern(
                    North: GrassId, East: GrassId, South: DirtId, West: DirtId,
                    NorthEast: GrassId, SouthEast: DirtId, SouthWest: GrassId, NorthWest: DirtId)),
                new(GrassOnlyPeerGraphic, new TerrainPattern(
                    North: GrassId, East: GrassId, South: GrassId, West: GrassId,
                    NorthEast: GrassId, SouthEast: GrassId, SouthWest: GrassId, NorthWest: GrassId))
            });
        var session = new TerrainEditorSession(baseline, CreateManifest());
        var before = Serialize(baseline);

        Assert.True(session.DeleteTerrain(GrassId));

        var current = session.CurrentCatalog;
        Assert.Single(current.Terrains);
        Assert.Equal(DirtId, current.Terrains[0].Id);
        Assert.DoesNotContain(current.Graphics, graphic => graphic.Reference == GrassGraphic);
        Assert.DoesNotContain(current.Graphics, graphic => graphic.Reference == GrassOnlyPeerGraphic);
        var dirt = current.Graphics.Single(graphic => graphic.Reference == DirtGraphic);
        Assert.Null(dirt.Pattern.South);
        var peer = current.Graphics.Single(graphic => graphic.Reference == PeerGraphic);
        Assert.Null(peer.Pattern.North);
        Assert.Null(peer.Pattern.East);
        Assert.Null(peer.Pattern.NorthEast);
        Assert.Null(peer.Pattern.SouthWest);
        Assert.Equal(DirtId, peer.Pattern.South);
        Assert.Equal(DirtId, peer.Pattern.West);
        Assert.Equal(DirtId, peer.Pattern.SouthEast);
        Assert.Equal(DirtId, peer.Pattern.NorthWest);

        Assert.True(session.Undo());
        Assert.Equal(before, Serialize(session.CurrentCatalog));
        Assert.True(session.Redo());
        Assert.Single(session.CurrentCatalog.Terrains);
        Assert.False(session.DeleteTerrain(GrassId));
    }

    [Fact]
    public void UndoAcrossSavedState_TracksDirtyExactly()
    {
        var session = CreateSession(out _);
        Assert.False(session.IsDirty);

        session.AddTerrain();
        Assert.True(session.IsDirty);

        session.MarkSaved(session.CurrentCatalog);
        Assert.False(session.IsDirty);
        Assert.True(session.CanUndo);

        Assert.True(session.Undo());
        Assert.True(session.IsDirty);
        Assert.True(session.Redo());
        Assert.False(session.IsDirty);
        Assert.True(session.Undo());
        Assert.True(session.IsDirty);
        Assert.True(session.Redo());
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void MarkSaved_AcceptedEdit_ClearsRedo()
    {
        var session = CreateSession(out _);
        session.AddTerrain();
        session.MarkSaved(session.CurrentCatalog);

        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        session.AddTerrain();
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void MarkSaved_PrepareApplyNotify_IsSplitBeforeFileMutation()
    {
        var session = CreateSession(out _);
        session.AddTerrain();
        Assert.True(session.IsDirty);
        var events = new List<TerrainEditorChangeKind>();
        session.Changed += events.Add;

        var prepared = session.PrepareMarkSaved(session.CurrentCatalog);
        Assert.True(session.IsDirty);
        Assert.Empty(events);

        session.ApplyMarkSaved(prepared);
        Assert.False(session.IsDirty);
        Assert.Empty(events);

        session.NotifyMarkSaved(prepared);
        Assert.Equal(new[] { TerrainEditorChangeKind.Committed }, events);
    }

    [Fact]
    public void RegionStroke_ClearingFinalAuthoredRegion_RemovesEntry_AndUndoRestores()
    {
        var session = CreateSession(out var baseline);
        var before = Serialize(baseline);

        session.BeginRegionStroke(null);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.Center));
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.North));
        Assert.True(session.CompleteRegionStroke());

        Assert.DoesNotContain(session.CurrentCatalog.Graphics, graphic => graphic.Reference == GrassGraphic);
        Assert.Contains(session.CurrentCatalog.Graphics, graphic => graphic.Reference == DirtGraphic);

        Assert.True(session.Undo());
        Assert.Equal(before, Serialize(session.CurrentCatalog));

        Assert.True(session.Redo());
        Assert.DoesNotContain(session.CurrentCatalog.Graphics, graphic => graphic.Reference == GrassGraphic);
    }

    [Fact]
    public void RegionStroke_KeepsCenterlessEntryWhileAnyPeerRemains()
    {
        var session = CreateSession(out var baseline);
        var before = Serialize(baseline);

        session.BeginRegionStroke(null);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.Center));
        Assert.True(session.CompleteRegionStroke());

        var graphic = session.CurrentCatalog.Graphics.Single(entry => entry.Reference == GrassGraphic);
        Assert.Null(graphic.Pattern.Center);
        Assert.Equal(DirtId, graphic.Pattern.North);

        Assert.True(session.Undo());
        var restored = session.CurrentCatalog.Graphics.Single(entry => entry.Reference == GrassGraphic);
        Assert.Equal(GrassId, restored.Pattern.Center);
        Assert.Equal(before, Serialize(session.CurrentCatalog));
    }

    [Fact]
    public void RegionStroke_OneDragIsOneCommand()
    {
        var session = CreateSession(out var baseline);
        var before = Serialize(baseline);

        session.BeginRegionStroke(DirtId);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.East));
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.South));
        session.VisitRegion(new TerrainRegionKey(DirtGraphic, TerrainPeer.North));
        session.VisitRegion(new TerrainRegionKey(DirtGraphic, TerrainPeer.West));
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);

        Assert.True(session.CompleteRegionStroke());
        Assert.True(session.CanUndo);
        Assert.True(session.IsDirty);

        Assert.True(session.Undo());
        Assert.Equal(before, Serialize(session.CurrentCatalog));
        Assert.True(session.Redo());
        var grass = session.CurrentCatalog.Graphics.Single(entry => entry.Reference == GrassGraphic);
        Assert.Equal(DirtId, grass.Pattern.East);
        Assert.Equal(DirtId, grass.Pattern.South);
    }

    [Fact]
    public void VisitRegion_FiresPreviewOnlyWhenEffective()
    {
        var session = CreateSession(out _);
        var events = new List<TerrainEditorChangeKind>();
        session.Changed += events.Add;

        Assert.Throws<InvalidOperationException>(() => session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.East)));

        session.BeginRegionStroke(GrassId);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.Center));
        Assert.Empty(events);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.East));
        Assert.Equal(new[] { TerrainEditorChangeKind.Preview }, events);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Throws<InvalidOperationException>(() => session.BeginRegionStroke(DirtId));

        Assert.True(session.CompleteRegionStroke());
        Assert.Equal(new[] { TerrainEditorChangeKind.Preview, TerrainEditorChangeKind.Committed }, events);
    }

    [Fact]
    public void CompleteRegionStroke_WithoutEffectiveVisits_IsNoOp()
    {
        var session = CreateSession(out _);
        var events = new List<TerrainEditorChangeKind>();
        session.Changed += events.Add;

        session.BeginRegionStroke(GrassId);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.Center));
        Assert.True(session.CompleteRegionStroke());
        Assert.False(session.CanUndo);
        Assert.Empty(events);
        Assert.False(session.IsDirty);
        Assert.False(session.CompleteRegionStroke());
    }

    [Fact]
    public void CancelRegionStroke_RestoresDraft_AndFiresPreview()
    {
        var session = CreateSession(out var baseline);
        var events = new List<TerrainEditorChangeKind>();
        session.Changed += events.Add;

        session.BeginRegionStroke(DirtId);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.East));
        session.CancelRegionStroke();
        Assert.Equal(new[] { TerrainEditorChangeKind.Preview, TerrainEditorChangeKind.Preview }, events);

        session.BeginRegionStroke(DirtId);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.East));
        Assert.True(session.CompleteRegionStroke());
        var expected = new TerrainCatalog(
            baseline.Terrains,
            new List<TerrainGraphicDefinition>
            {
                new(GrassGraphic, new TerrainPattern(Center: GrassId, North: DirtId, East: DirtId)),
                new(DirtGraphic, new TerrainPattern(Center: DirtId, South: GrassId))
            });
        Assert.Equal(expected, session.CurrentCatalog);
        Assert.Equal(
            new[]
            {
                TerrainEditorChangeKind.Preview,
                TerrainEditorChangeKind.Preview,
                TerrainEditorChangeKind.Preview,
                TerrainEditorChangeKind.Committed
            },
            events);

        session.BeginRegionStroke(GrassId);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.Center));
        session.CancelRegionStroke();
        Assert.Equal(4, events.Count);
        session.CancelRegionStroke();
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public void ActiveStroke_GuardsRejectOperations()
    {
        var session = CreateSession(out var baseline);
        session.BeginRegionStroke(GrassId);

        Assert.Throws<InvalidOperationException>(() => session.AddTerrain());
        Assert.Throws<InvalidOperationException>(() => session.RenameTerrain(GrassId, "X"));
        Assert.Throws<InvalidOperationException>(() => session.SetColorOverride(GrassId, null));
        Assert.Throws<InvalidOperationException>(() => session.DeleteTerrain(GrassId));
        Assert.Throws<InvalidOperationException>(() => session.Undo());
        Assert.Throws<InvalidOperationException>(() => session.Redo());
        Assert.Throws<InvalidOperationException>(() => session.Revert());
        Assert.Throws<InvalidOperationException>(() => session.MarkSaved(baseline));
        Assert.Throws<InvalidOperationException>(() => session.BeginRegionStroke(null));

        session.CancelRegionStroke();
        Assert.Equal(baseline, session.CurrentCatalog);
    }

    [Fact]
    public void Revert_RestoresLastSavedBaseline_AndClearsHistory()
    {
        var session = CreateSession(out _);
        var id = session.AddTerrain();
        Assert.True(session.RenameTerrain(id, "Saved"));
        session.MarkSaved(session.CurrentCatalog);
        var saved = session.CurrentCatalog;

        session.AddTerrain();
        Assert.True(session.RenameTerrain(GrassId, "Changed"));
        Assert.True(session.IsDirty);

        session.Revert();

        Assert.Equal(saved, session.CurrentCatalog);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void CanSave_FollowsValidation()
    {
        var session = CreateSession(out _);
        Assert.True(session.CanSave);

        Assert.True(session.RenameTerrain(DirtId, "grass"));
        Assert.False(session.CanSave);
        Assert.Contains(session.Diagnostics, issue => issue.Code == TerrainValidationCode.DuplicateTerrainName);

        Assert.True(session.Undo());
        Assert.True(session.CanSave);

        session.BeginRegionStroke(Guid.NewGuid());
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.East));
        Assert.True(session.CompleteRegionStroke());
        Assert.False(session.CanSave);
        Assert.Contains(session.Diagnostics, issue => issue.Code == TerrainValidationCode.UnknownPeer);
    }

    [Fact]
    public void Undo_RenameAndColor_RestoresExactCatalog()
    {
        var session = CreateSession(out var baseline);
        var before = Serialize(baseline);

        Assert.True(session.RenameTerrain(GrassId, "Renamed"));
        Assert.True(session.SetColorOverride(GrassId, new TerrainColor(1, 1, 1)));

        Assert.True(session.Undo());
        Assert.True(session.Undo());
        Assert.Equal(before, Serialize(session.CurrentCatalog));

        Assert.True(session.Redo());
        Assert.Equal("Renamed", NameOf(session, GrassId));
        Assert.True(session.Redo());
        var color = session.CurrentCatalog.Terrains.Single(terrain => terrain.Id == GrassId).ColorOverride;
        Assert.Equal(new TerrainColor(1, 1, 1), color);
    }

    [Fact]
    public void Preview_DoesNotExposeDraftThroughCurrentCatalog()
    {
        var session = CreateSession(out var baseline);

        session.BeginRegionStroke(DirtId);
        session.VisitRegion(new TerrainRegionKey(GrassGraphic, TerrainPeer.East));

        Assert.Equal(baseline, session.CurrentCatalog);
        session.CancelRegionStroke();
    }
}
