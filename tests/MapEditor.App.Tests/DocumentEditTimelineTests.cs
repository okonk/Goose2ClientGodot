using System;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Editing;
using MapEditor.GameData.Rows;
using Xunit;

namespace MapEditor.App.Tests;

public class DocumentEditTimelineTests
{
    private static NpcSpawnRow Spawn(int npcId, int mapX, int mapY) => new(npcId, 10, mapX, mapY);

    private static void Paint(MapEditSession session, int x, int y, int layerIndex, MapTileLayer tile)
    {
        session.SelectedLayers = (byte)(1 << layerIndex);
        session.SelectedTileLayer = tile;
        session.BeginStroke(MapEditTool.Pencil, x, y);
        Assert.True(session.CompleteStroke());
    }

    private static void Paste(MapEditSession session, int x, int y)
    {
        var patch = new MapTileLayer[MapDocument.LayerCount][];
        patch[0] = new[] { new MapTileLayer(4, 4), new MapTileLayer(5, 5) };
        Assert.True(session.ApplyLayerPatch(x, y, 2, 1, patch));
    }

    [Fact]
    public void InterleavedMapAndSheetEdits_UndoAndRedoInExactReverseAndForwardOrder()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4));
        var sheet = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        Paint(map, 0, 0, 1, new MapTileLayer(1, 2));
        sheet.AddSpawn(Spawn(1, 5, 6));
        Paste(map, 2, 2);

        Assert.True(timeline.CanUndo);
        Assert.False(timeline.CanRedo);

        Assert.True(timeline.Undo());
        Assert.Equal(new MapTileLayer(0, 0), map.Document[2, 2].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), map.Document[3, 2].GetLayer(0));
        Assert.Equal(new MapTileLayer(1, 2), map.Document[0, 0].GetLayer(1));
        Assert.Equal(new[] { Spawn(1, 5, 6) }, sheet.Spawns);

        Assert.True(timeline.Undo());
        Assert.Equal(new MapTileLayer(1, 2), map.Document[0, 0].GetLayer(1));
        Assert.Empty(sheet.Spawns);

        Assert.True(timeline.Undo());
        Assert.Equal(new MapTileLayer(0, 0), map.Document[0, 0].GetLayer(1));
        Assert.Empty(sheet.Spawns);
        Assert.False(timeline.CanUndo);
        Assert.True(timeline.CanRedo);

        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(1, 2), map.Document[0, 0].GetLayer(1));
        Assert.Empty(sheet.Spawns);

        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(0, 0), map.Document[2, 2].GetLayer(0));
        Assert.Equal(new[] { Spawn(1, 5, 6) }, sheet.Spawns);

        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(4, 4), map.Document[2, 2].GetLayer(0));
        Assert.Equal(new MapTileLayer(5, 5), map.Document[3, 2].GetLayer(0));
        Assert.False(timeline.CanRedo);
        Assert.True(timeline.CanUndo);
    }

    [Fact]
    public void NewSheetEditAfterMapUndos_MakesRedoUnavailableInBothDomains()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4));
        var sheet = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        Paint(map, 0, 0, 1, new MapTileLayer(1, 2));
        Paint(map, 1, 1, 2, new MapTileLayer(1, 2));
        Assert.True(timeline.Undo());
        Assert.True(timeline.Undo());
        Assert.True(map.CanRedo);
        Assert.True(timeline.CanRedo);

        sheet.AddSpawn(Spawn(1, 5, 6));

        Assert.False(map.CanRedo);
        Assert.False(sheet.CanRedo);
        Assert.False(timeline.CanRedo);
        Assert.True(timeline.CanUndo);
        Assert.Equal(new MapTileLayer(0, 0), map.Document[0, 0].GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), map.Document[1, 1].GetLayer(2));
        Assert.Equal(new[] { Spawn(1, 5, 6) }, sheet.Spawns);
    }

    [Fact]
    public void MapSaveAndSheetPushBaselines_AreTrackedIndependently()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4));
        var sheet = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        Paint(map, 0, 0, 1, new MapTileLayer(1, 2));
        Assert.True(map.IsDirty);
        Assert.False(sheet.IsDirty);

        sheet.AddSpawn(Spawn(1, 5, 6));
        Assert.True(map.IsDirty);
        Assert.True(sheet.IsDirty);

        map.MarkSaved();
        Assert.False(map.IsDirty);
        Assert.True(sheet.IsDirty);

        sheet.MarkPushed();
        Assert.False(map.IsDirty);
        Assert.False(sheet.IsDirty);

        Assert.True(timeline.Undo());
        Assert.False(map.IsDirty);
        Assert.True(sheet.IsDirty);

        Assert.True(timeline.Undo());
        Assert.True(map.IsDirty);
        Assert.True(sheet.IsDirty);
    }

    [Fact]
    public void CompoundEntry_OneUndoAndOneRedoRestoreAndReapplyBothDomains()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4));
        map.Document.SetLayer(3, 3, 0, new MapTileLayer(8, 8));
        var sheet = new SheetEditSession(
            new[] { Spawn(1, 5, 6), Spawn(2, 7, 8), Spawn(3, 9, 10) },
            Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        bool applied = timeline.ApplyCompound(
            mapSession => mapSession.ApplyResize(new MapTileRectangle(0, 0, 3, 3)),
            sheetSession =>
            {
                sheetSession.ReplaceSpawns(new[] { Spawn(3, 9, 10), Spawn(1, 5, 6) });
                return true;
            });

        Assert.True(applied);
        Assert.Equal(3, map.Document.Width);
        Assert.Equal(3, map.Document.Height);
        Assert.Equal(new[] { Spawn(3, 9, 10), Spawn(1, 5, 6) }, sheet.Spawns);
        Assert.True(timeline.CanUndo);
        Assert.False(timeline.CanRedo);

        Assert.True(timeline.Undo());
        Assert.Equal(4, map.Document.Width);
        Assert.Equal(4, map.Document.Height);
        Assert.Equal(new MapTileLayer(8, 8), map.Document[3, 3].GetLayer(0));
        Assert.Equal(new[] { Spawn(1, 5, 6), Spawn(2, 7, 8), Spawn(3, 9, 10) }, sheet.Spawns);

        Assert.True(timeline.Redo());
        Assert.Equal(3, map.Document.Width);
        Assert.Equal(3, map.Document.Height);
        Assert.Equal(new[] { Spawn(3, 9, 10), Spawn(1, 5, 6) }, sheet.Spawns);
        Assert.False(timeline.CanRedo);
        Assert.True(timeline.CanUndo);
    }

    [Fact]
    public void Undo_WhenOldestMapCommandEvictedByCap_DropsStaleEntryAndLeavesDocumentUntouched()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4), retainedHistoryCapBytes: 3 * 224);
        var sheet = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        Paint(map, 0, 0, 1, new MapTileLayer(1, 2));
        Paint(map, 1, 1, 1, new MapTileLayer(3, 4));
        Paint(map, 2, 2, 1, new MapTileLayer(5, 6));
        Paint(map, 3, 3, 1, new MapTileLayer(7, 8));

        Assert.True(timeline.Undo());
        Assert.True(timeline.Undo());
        Assert.True(timeline.Undo());

        Assert.False(timeline.Undo());
        Assert.Equal(new MapTileLayer(1, 2), map.Document[0, 0].GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), map.Document[1, 1].GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), map.Document[2, 2].GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), map.Document[3, 3].GetLayer(1));
        Assert.False(timeline.CanUndo);

        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(3, 4), map.Document[1, 1].GetLayer(1));
        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(5, 6), map.Document[2, 2].GetLayer(1));
        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(7, 8), map.Document[3, 3].GetLayer(1));
        Assert.False(timeline.CanRedo);
    }

    [Fact]
    public void CompoundEntry_SecondDomainFails_ReplaysFirstDomainAndPublishesNoEntry()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4));
        map.Document.SetLayer(3, 3, 0, new MapTileLayer(8, 8));
        var sheet = new SheetEditSession(
            new[] { Spawn(1, 5, 6), Spawn(2, 7, 8), Spawn(3, 9, 10) },
            Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        bool applied = timeline.ApplyCompound(
            mapSession => mapSession.ApplyResize(new MapTileRectangle(0, 0, 3, 3)),
            sheetSession => false);

        Assert.False(applied);
        Assert.Equal(4, map.Document.Width);
        Assert.Equal(4, map.Document.Height);
        Assert.Equal(new MapTileLayer(8, 8), map.Document[3, 3].GetLayer(0));
        Assert.Equal(3, sheet.Spawns.Count);
        Assert.False(timeline.CanUndo);
        Assert.False(timeline.CanRedo);
    }

    [Fact]
    public void CompoundEntry_FirstDomainFails_ReplaysNeitherDomainAndPublishesNoEntry()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4));
        map.Document.SetLayer(3, 3, 0, new MapTileLayer(8, 8));
        var sheet = new SheetEditSession(
            new[] { Spawn(1, 5, 6), Spawn(2, 7, 8), Spawn(3, 9, 10) },
            Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        bool applied = timeline.ApplyCompound(
            mapSession => false,
            sheetSession =>
            {
                sheetSession.ReplaceSpawns(new[] { Spawn(3, 9, 10), Spawn(1, 5, 6) });
                return true;
            });

        Assert.False(applied);
        Assert.Equal(4, map.Document.Width);
        Assert.Equal(4, map.Document.Height);
        Assert.Equal(new MapTileLayer(8, 8), map.Document[3, 3].GetLayer(0));
        Assert.Equal(3, sheet.Spawns.Count);
        Assert.False(timeline.CanUndo);
        Assert.False(timeline.CanRedo);
    }

    [Fact]
    public void CompoundEntry_OperationThatReturnsTrueWithoutPushing_ThrowsAndLeavesDocumentUntouched()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4));
        map.Document.SetLayer(3, 3, 0, new MapTileLayer(8, 8));
        var sheet = new SheetEditSession(
            new[] { Spawn(1, 5, 6) },
            Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        Assert.Throws<InvalidOperationException>(() => timeline.ApplyCompound(
            mapSession => true,
            sheetSession => true));

        Assert.Equal(4, map.Document.Width);
        Assert.Equal(new MapTileLayer(8, 8), map.Document[3, 3].GetLayer(0));
        Assert.Equal(1, sheet.Spawns.Count);
        Assert.False(timeline.CanUndo);
        Assert.False(timeline.CanRedo);
    }

    [Fact]
    public void CompoundEntry_MisalignedSeam_ReplaysNeitherDomain()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4));
        var sheet = new SheetEditSession(
            new[] { Spawn(1, 5, 6), Spawn(2, 7, 8), Spawn(3, 9, 10) },
            Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        Assert.True(timeline.ApplyCompound(
            mapSession => mapSession.ApplyResize(new MapTileRectangle(0, 0, 3, 3)),
            sheetSession =>
            {
                sheetSession.ReplaceSpawns(new[] { Spawn(3, 9, 10), Spawn(1, 5, 6) });
                return true;
            }));

        timeline.WithRecordingSuppressed(() => Paint(map, 0, 0, 1, new MapTileLayer(1, 2)));

        Assert.False(timeline.Undo());
        Assert.Equal(3, map.Document.Width);
        Assert.Equal(new MapTileLayer(1, 2), map.Document[0, 0].GetLayer(1));
        Assert.Equal(new[] { Spawn(3, 9, 10), Spawn(1, 5, 6) }, sheet.Spawns);
        Assert.True(timeline.CanUndo);
        Assert.False(timeline.CanRedo);
    }

    [Fact]
    public void FailedCompound_MappedAppliedAndRolledBack_PreExistingEntryRemainsFullyUsable()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4));
        var sheet = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        Paint(map, 0, 0, 1, new MapTileLayer(1, 2));

        bool applied = timeline.ApplyCompound(
            mapSession => mapSession.ApplyResize(new MapTileRectangle(0, 0, 3, 3)),
            sheetSession => false);

        Assert.False(applied);
        Assert.Equal(4, map.Document.Width);
        Assert.Equal(4, map.Document.Height);
        Assert.Equal(new MapTileLayer(1, 2), map.Document[0, 0].GetLayer(1));
        Assert.Empty(sheet.Spawns);
        Assert.True(timeline.CanUndo);
        Assert.False(timeline.CanRedo);

        Assert.True(timeline.Undo());
        Assert.Equal(new MapTileLayer(0, 0), map.Document[0, 0].GetLayer(1));
        Assert.False(timeline.CanUndo);
        Assert.True(timeline.CanRedo);

        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(1, 2), map.Document[0, 0].GetLayer(1));
        Assert.True(timeline.CanUndo);
        Assert.False(timeline.CanRedo);
    }

    [Fact]
    public void CompoundUndo_WhenMapSideEvictedByCap_FailsWithoutMutationAndRemainingEntriesStayUsable()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4), retainedHistoryCapBytes: 3 * 224);
        var sheet = new SheetEditSession(
            new[] { Spawn(1, 5, 6), Spawn(2, 7, 8), Spawn(3, 9, 10) },
            Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        bool applied = timeline.ApplyCompound(
            mapSession =>
            {
                Paint(mapSession, 1, 1, 1, new MapTileLayer(2, 2));
                return true;
            },
            sheetSession =>
            {
                sheetSession.ReplaceSpawns(new[] { Spawn(3, 9, 10), Spawn(1, 5, 6) });
                return true;
            });

        Assert.True(applied);
        Paint(map, 2, 2, 1, new MapTileLayer(4, 4));
        Paint(map, 3, 3, 1, new MapTileLayer(6, 6));
        Paint(map, 0, 2, 1, new MapTileLayer(8, 8));
        sheet.AddSpawn(Spawn(4, 11, 12));

        Assert.True(timeline.Undo());
        Assert.Equal(new[] { Spawn(3, 9, 10), Spawn(1, 5, 6) }, sheet.Spawns);
        Assert.True(timeline.Undo());
        Assert.True(timeline.Undo());
        Assert.True(timeline.Undo());

        Assert.False(timeline.Undo());
        Assert.Equal(new MapTileLayer(2, 2), map.Document[1, 1].GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), map.Document[2, 2].GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), map.Document[3, 3].GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), map.Document[0, 2].GetLayer(1));
        Assert.Equal(new[] { Spawn(3, 9, 10), Spawn(1, 5, 6) }, sheet.Spawns);
        Assert.False(timeline.CanUndo);
        Assert.True(timeline.CanRedo);

        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(4, 4), map.Document[2, 2].GetLayer(1));
        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(6, 6), map.Document[3, 3].GetLayer(1));
        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(8, 8), map.Document[0, 2].GetLayer(1));
        Assert.True(timeline.Redo());
        Assert.Equal(new[] { Spawn(3, 9, 10), Spawn(1, 5, 6), Spawn(4, 11, 12) }, sheet.Spawns);
        Assert.False(timeline.CanRedo);
        Assert.True(timeline.CanUndo);
    }

    [Fact]
    public void SuppressedEditInOneDomain_BlocksUndoOfThatDomainsEntryOnly()
    {
        var map = new MapEditSession(MapDocument.Create(4, 4));
        var sheet = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        var timeline = new DocumentEditTimeline(map, sheet);

        sheet.AddSpawn(Spawn(1, 5, 6));
        Paint(map, 0, 0, 1, new MapTileLayer(1, 2));
        timeline.WithRecordingSuppressed(() => Paint(map, 1, 1, 2, new MapTileLayer(2, 3)));

        Assert.False(timeline.Undo());
        Assert.Equal(new MapTileLayer(1, 2), map.Document[0, 0].GetLayer(1));
        Assert.Equal(new MapTileLayer(2, 3), map.Document[1, 1].GetLayer(2));
        Assert.Equal(new[] { Spawn(1, 5, 6) }, sheet.Spawns);
        Assert.True(timeline.CanUndo);
    }
}
