using System;
using System.Collections.Generic;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;
using Xunit;

namespace MapEditor.App.Tests;

public class GameDataClipboardTests : IDisposable
{
    private static readonly MapReference Map10 = new(10, "Dungeon", "dungeon.bytes");
    private static readonly MapReference Map20 = new(20, "Cave", "cave.bytes");
    private static readonly IReadOnlyList<MapReference> Maps = new[] { Map10, Map20 };
    private static readonly NpcAppearance Npc1 = new(1, "Goose", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);

    private readonly FakeEditorDialogs _dialogs = new();
    private readonly WorkspaceViewModel _workspace;
    private readonly MapDocumentViewModel _source;
    private readonly MapDocumentViewModel _sameSheet;
    private readonly MapDocumentViewModel _otherSheet;
    private readonly MapDocumentViewModel _unpulled;

    public GameDataClipboardTests()
    {
        _workspace = new WorkspaceViewModel(_dialogs, new MapFileStore());
        _source = _workspace.ActiveDocument;
        _source.GameData!.AttachSession(Session("sheet-a", 10));

        _sameSheet = CreateDocument();
        _sameSheet.GameData!.AttachSession(Session("sheet-a", 20));

        _otherSheet = CreateDocument();
        _otherSheet.GameData!.AttachSession(Session("sheet-b", 10));

        _unpulled = CreateDocument();
    }

    public void Dispose()
    {
        foreach (MapDocumentViewModel document in _workspace.Documents)
        {
            document.Dispose();
        }
    }

    private MapDocumentViewModel CreateDocument()
    {
        _dialogs.NewMapResult = new NewMapRequest(100, 100);
        _workspace.NewAsync().GetAwaiter().GetResult();
        return _workspace.ActiveDocument;
    }

    private static GameDataSyncSession Session(string spreadsheetId, int mapId)
    {
        var data = new RemoteGameData(
            Maps,
            new Dictionary<int, NpcAppearance> { [1] = Npc1 },
            new List<RemoteRow<NpcSpawnRow>>
            {
                new(2, new NpcSpawnRow(1, mapId, 3, 4)),
                new(3, new NpcSpawnRow(1, mapId, 7, 8))
            },
            new List<RemoteRow<WarpRow>> { new(2, new WarpRow(mapId, 5, 6, 20, 7, 8)) });
        return new GameDataSyncSession(spreadsheetId, mapId, data);
    }

    private static void SelectSpawn(MapDocumentViewModel document, int index)
    {
        document.GameData!.ActiveTool = GameDataTool.Spawn;
        document.GameData.SelectedSpawn = index;
    }

    private static void SelectWarp(MapDocumentViewModel document, int index)
    {
        document.GameData!.ActiveTool = GameDataTool.Warp;
        document.GameData.SelectedWarp = index;
    }

    [Fact]
    public void CopySelectedSpawn_CapturesNpcIdAndSpreadsheetOnly()
    {
        SelectSpawn(_source, 0);
        _source.CopySelection();

        EditorClipboardPayload payload = _workspace.Clipboard.Current!;
        Assert.Equal(EditorClipboardKind.Spawn, payload.Kind);
        Assert.Equal(1, payload.SpawnNpcId);
        Assert.Equal("sheet-a", payload.SourceSpreadsheetId);
        Assert.Null(payload.Tiles);
        Assert.Null(payload.WarpDestinationMapId);
        Assert.Null(payload.WarpDestinationX);
        Assert.Null(payload.WarpDestinationY);
        Assert.Null(_source.Clipboard);
        Assert.Equal(2, _source.GameData!.Session!.Edits.Spawns.Count);
        Assert.False(_source.GameData.IsDirty);
    }

    [Fact]
    public void CopySelectedWarp_CapturesDestinationFieldsOnly()
    {
        SelectWarp(_source, 0);
        _source.CopySelection();

        EditorClipboardPayload payload = _workspace.Clipboard.Current!;
        Assert.Equal(EditorClipboardKind.Warp, payload.Kind);
        Assert.Equal(20, payload.WarpDestinationMapId);
        Assert.Equal(7, payload.WarpDestinationX);
        Assert.Equal(8, payload.WarpDestinationY);
        Assert.Equal("sheet-a", payload.SourceSpreadsheetId);
        Assert.Null(payload.Tiles);
        Assert.Null(payload.SpawnNpcId);
        Assert.Single(_source.GameData!.Session!.Edits.Warps);
        Assert.False(_source.GameData.IsDirty);
    }

    [Fact]
    public void CutSelectedSpawn_IsOneReversibleSheetEdit()
    {
        SelectSpawn(_source, 0);
        long versionBefore = _source.GameData!.Session!.Edits.HistoryVersion;
        _source.CutSelection();

        Assert.Equal(EditorClipboardKind.Spawn, _workspace.Clipboard.Current!.Kind);
        Assert.Single(_source.GameData.Session.Edits.Spawns);
        Assert.True(_source.GameData.IsDirty);
        Assert.Null(_source.GameData.SelectedSpawn);
        Assert.False(_source.Session.IsDirty);
        Assert.Equal(1, _source.GameData.Session.Edits.HistoryVersion - versionBefore);

        Assert.True(_source.Undo());
        var spawns = _source.GameData.Session.Edits.Spawns;
        Assert.Equal(2, spawns.Count);
        Assert.Equal(new NpcSpawnRow(1, 10, 3, 4), spawns[0]);
        Assert.True(_source.CanRedo);

        Assert.True(_source.Redo());
        Assert.Single(_source.GameData.Session.Edits.Spawns);
    }

    [Fact]
    public void CutSelectedWarp_IsOneReversibleSheetEdit()
    {
        SelectWarp(_source, 0);
        long versionBefore = _source.GameData!.Session!.Edits.HistoryVersion;
        _source.CutSelection();

        Assert.Equal(EditorClipboardKind.Warp, _workspace.Clipboard.Current!.Kind);
        Assert.Empty(_source.GameData.Session.Edits.Warps);
        Assert.True(_source.GameData.IsDirty);
        Assert.Null(_source.GameData.SelectedWarp);
        Assert.False(_source.Session.IsDirty);
        Assert.Equal(1, _source.GameData.Session.Edits.HistoryVersion - versionBefore);

        Assert.True(_source.Undo());
        var warps = _source.GameData.Session.Edits.Warps;
        Assert.Single(warps);
        Assert.Equal(new WarpRow(10, 5, 6, 20, 7, 8), warps[0]);
        Assert.True(_source.CanRedo);

        Assert.True(_source.Redo());
        Assert.Empty(_source.GameData.Session.Edits.Warps);
    }

    [Fact]
    public void PasteSpawn_SameTab_EntersPasteModeAndAppliesAtTheClickedTile()
    {
        SelectSpawn(_source, 0);
        _source.CopySelection();

        _source.PasteSelection();

        Assert.True(_source.PasteMode);
        Assert.Equal(2, _source.GameData!.Session!.Edits.Spawns.Count);

        _source.ApplyPasteAt(9, 10);

        var spawns = _source.GameData.Session.Edits.Spawns;
        Assert.Equal(3, spawns.Count);
        Assert.Equal(new NpcSpawnRow(1, 10, 9, 10), spawns[2]);
        Assert.Equal(2, _source.GameData.SelectedSpawn);
        Assert.False(_source.PasteMode);
        Assert.True(_source.CanUndo);
    }

    [Fact]
    public void PasteSpawn_SameSpreadsheetOtherTab_UsesTheDestinationMapId()
    {
        SelectSpawn(_source, 0);
        _source.CopySelection();

        _sameSheet.PasteSelection();

        Assert.True(_sameSheet.PasteMode);

        _sameSheet.ApplyPasteAt(2, 3);

        var spawns = _sameSheet.GameData!.Session!.Edits.Spawns;
        Assert.Equal(3, spawns.Count);
        Assert.Equal(new NpcSpawnRow(1, 20, 2, 3), spawns[2]);
        Assert.Equal(2, _sameSheet.GameData.SelectedSpawn);
        Assert.False(_sameSheet.PasteMode);
    }

    [Fact]
    public void PasteSpawn_DifferentSpreadsheet_IsRejectedWithoutMutation()
    {
        SelectSpawn(_source, 0);
        _source.CopySelection();
        _otherSheet.SelectedX = 2;
        _otherSheet.SelectedY = 3;
        long versionBefore = _otherSheet.GameData!.Session!.Edits.HistoryVersion;
        var errors = new List<ErrorPresentation>();
        _otherSheet.GameDataError += error => errors.Add(error);

        _otherSheet.PasteSelection();

        Assert.Single(errors);
        Assert.Equal(2, _otherSheet.GameData.Session.Edits.Spawns.Count);
        Assert.Equal(versionBefore, _otherSheet.GameData.Session.Edits.HistoryVersion);
        Assert.False(_otherSheet.CanUndo);
        Assert.False(_otherSheet.PasteMode);
        Assert.NotNull(_workspace.Clipboard.Current);
    }

    [Fact]
    public void PasteSpawn_UnpulledTab_IsRejectedWithoutMutation()
    {
        SelectSpawn(_source, 0);
        _source.CopySelection();
        _unpulled.SelectedX = 2;
        _unpulled.SelectedY = 3;
        var errors = new List<ErrorPresentation>();
        _unpulled.GameDataError += error => errors.Add(error);

        _unpulled.PasteSelection();

        Assert.Single(errors);
        Assert.False(_unpulled.PasteMode);
        Assert.NotNull(_workspace.Clipboard.Current);
    }

    [Fact]
    public void PasteSpawn_WithoutSelectedTile_EntersPasteModeAndAppliesAtTheClickedTile()
    {
        SelectSpawn(_source, 0);
        _source.CopySelection();

        _source.PasteSelection();

        Assert.True(_source.PasteMode);
        Assert.Equal(2, _source.GameData!.Session!.Edits.Spawns.Count);

        _source.ApplyPasteAt(1, 1);

        var spawns = _source.GameData.Session.Edits.Spawns;
        Assert.Equal(3, spawns.Count);
        Assert.Equal(new NpcSpawnRow(1, 10, 1, 1), spawns[2]);
        Assert.False(_source.PasteMode);
    }

    [Fact]
    public void PasteWarp_SameSpreadsheetOtherTab_SourcesAtTheClickedTileKeepingTheDestination()
    {
        SelectWarp(_source, 0);
        _source.CopySelection();

        _sameSheet.PasteSelection();

        Assert.True(_sameSheet.PasteMode);

        _sameSheet.ApplyPasteAt(4, 5);

        var warps = _sameSheet.GameData!.Session!.Edits.Warps;
        Assert.Equal(2, warps.Count);
        Assert.Equal(new WarpRow(20, 4, 5, 20, 7, 8), warps[1]);
        Assert.Equal(1, _sameSheet.GameData.SelectedWarp);
        Assert.False(_sameSheet.PasteMode);
    }

    [Fact]
    public void PasteWarp_SameTab_EntersPasteModeAndAppliesAtTheClickedTileKeepingTheDestination()
    {
        SelectWarp(_source, 0);
        _source.CopySelection();
        long versionBefore = _source.GameData!.Session!.Edits.HistoryVersion;

        _source.PasteSelection();

        Assert.True(_source.PasteMode);

        _source.ApplyPasteAt(8, 9);

        var warps = _source.GameData.Session.Edits.Warps;
        Assert.Equal(2, warps.Count);
        Assert.Equal(new WarpRow(10, 8, 9, 20, 7, 8), warps[1]);
        Assert.Equal(new WarpRow(10, 5, 6, 20, 7, 8), warps[0]);
        Assert.Equal(1, _source.GameData.SelectedWarp);
        Assert.Equal(1, _source.GameData.Session.Edits.HistoryVersion - versionBefore);
        Assert.False(_source.PasteMode);
    }

    [Fact]
    public void PasteWarp_UnpulledTab_IsRejectedWithoutMutation()
    {
        SelectWarp(_source, 0);
        _source.CopySelection();
        _unpulled.SelectedX = 4;
        _unpulled.SelectedY = 5;
        var errors = new List<ErrorPresentation>();
        _unpulled.GameDataError += error => errors.Add(error);

        _unpulled.PasteSelection();

        Assert.Single(errors);
        Assert.False(_unpulled.PasteMode);
        Assert.Null(_unpulled.GameData.Session);
        Assert.NotNull(_workspace.Clipboard.Current);
    }

    [Fact]
    public void PasteWarp_WithoutSelectedTile_EntersPasteModeAndAppliesAtTheClickedTile()
    {
        SelectWarp(_source, 0);
        _source.CopySelection();
        long versionBefore = _source.GameData!.Session!.Edits.HistoryVersion;

        _source.PasteSelection();

        Assert.True(_source.PasteMode);

        _source.ApplyPasteAt(0, 0);

        var warps = _source.GameData.Session.Edits.Warps;
        Assert.Equal(2, warps.Count);
        Assert.Equal(new WarpRow(10, 0, 0, 20, 7, 8), warps[1]);
        Assert.Equal(1, _source.GameData.Session.Edits.HistoryVersion - versionBefore);
        Assert.False(_source.PasteMode);
    }

    [Fact]
    public void PasteWarp_DifferentSpreadsheet_IsRejectedWithoutMutation()
    {
        SelectWarp(_source, 0);
        _source.CopySelection();
        _otherSheet.SelectedX = 4;
        _otherSheet.SelectedY = 5;
        long versionBefore = _otherSheet.GameData!.Session!.Edits.HistoryVersion;
        var errors = new List<ErrorPresentation>();
        _otherSheet.GameDataError += error => errors.Add(error);

        _otherSheet.PasteSelection();

        Assert.Single(errors);
        Assert.Equal(1, _otherSheet.GameData.Session.Edits.Warps.Count);
        Assert.Equal(versionBefore, _otherSheet.GameData.Session.Edits.HistoryVersion);
        Assert.False(_otherSheet.CanUndo);
        Assert.False(_otherSheet.PasteMode);
        Assert.NotNull(_workspace.Clipboard.Current);
    }

    [Fact]
    public void CopySelection_MapToolWithGameDataSelection_CopiesTilesOnly()
    {
        _source.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(3, 3));
        _source.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        SelectSpawn(_source, 0);
        _source.ActiveTool = MapEditTool.MultiSelect;

        _source.CopySelection();

        EditorClipboardPayload payload = _workspace.Clipboard.Current!;
        Assert.Equal(EditorClipboardKind.Tiles, payload.Kind);
        Assert.NotNull(payload.Tiles);
        Assert.Null(payload.SpawnNpcId);
        Assert.Null(payload.WarpDestinationMapId);
        Assert.Null(payload.SourceSpreadsheetId);
    }

    [Fact]
    public void CopySelection_SpawnToolWithTileSelection_CopiesTheSpawn()
    {
        _source.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(3, 3));
        _source.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        SelectSpawn(_source, 0);

        _source.CopySelection();

        Assert.Equal(EditorClipboardKind.Spawn, _workspace.Clipboard.Current!.Kind);
    }

    [Fact]
    public void CutSelection_MapToolWithGameDataSelection_CutsTilesOnly()
    {
        _source.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(3, 3));
        _source.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        SelectSpawn(_source, 0);
        _source.ActiveTool = MapEditTool.MultiSelect;

        _source.CutSelection();

        Assert.Equal(EditorClipboardKind.Tiles, _workspace.Clipboard.Current!.Kind);
        Assert.Equal(new MapTileLayer(0, 0), _source.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(2, _source.GameData!.Session!.Edits.Spawns.Count);
        Assert.False(_source.GameData.IsDirty);
    }

    [Fact]
    public void PasteSelection_WithTilePayload_EntersTilePasteModeWithoutTouchingGameData()
    {
        _source.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        _source.CopySelection();
        _source.SelectedX = 0;
        _source.SelectedY = 0;

        _source.PasteSelection();

        Assert.True(_source.PasteMode);
        Assert.Equal(2, _source.GameData!.Session!.Edits.Spawns.Count);
        Assert.False(_source.CanUndo);
    }
}
