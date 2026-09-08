using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapEditor.App.Connectivity;
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

public class EditorDocumentControllerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-controller-").FullName;
    private readonly FakeEditorDialogs _dialogs = new();
    private readonly MapFileStore _store = new();
    private readonly EditorDocumentController _controller;

    public EditorDocumentControllerTests()
    {
        _controller = new EditorDocumentController(_dialogs, _store, InitialDocument());
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private string MapPath(string name) => Path.Combine(_directory, name);

    private static EditorDocument InitialDocument()
        => new(new MapEditSession(MapDocument.Create(), initiallyDirty: false), null, null);

    private void Paint(MapEditSession session, int x, int y, MapTileLayer layer)
    {
        session.SelectedTileLayer = layer;
        session.BeginStroke(MapEditTool.Pencil, x, y);
        Assert.True(session.CompleteStroke());
    }

    [Fact]
    public void Startup_IsCleanUntitledDocumentWithEmptyHistory()
    {
        EditorDocument document = _controller.Document;

        Assert.False(document.Session.IsDirty);
        Assert.Null(document.Path);
        Assert.Null(document.Revision);
        Assert.Equal(MapDocument.DefaultWidth, document.Session.Document.Width);
        Assert.Equal(MapDocument.DefaultHeight, document.Session.Document.Height);
        Assert.False(document.Session.CanUndo);
        Assert.False(document.Session.CanRedo);
        Assert.False(document.Session.HasActiveStroke);
    }

    [Fact]
    public async Task SaveAs_ThenSave_UpdatesFileWithoutPicker()
    {
        string path = MapPath("save.map");
        _dialogs.SavePickResult = path;

        await _controller.SaveAsAsync();

        Assert.Equal("Untitled", _dialogs.LastSaveSuggestedName);
        EditorDocument afterFirst = _controller.Document;
        Assert.Equal(Path.GetFullPath(path), afterFirst.Path);
        MapFileRevision firstRevision = afterFirst.Revision!.Value;
        Assert.False(afterFirst.Session.IsDirty);
        int savePicks = _dialogs.SavePickShown;

        Paint(afterFirst.Session, 0, 0, new MapTileLayer(4, 9));
        Assert.True(afterFirst.Session.IsDirty);

        await _controller.SaveAsync();

        Assert.Equal(savePicks, _dialogs.SavePickShown);
        Assert.NotEqual(firstRevision, _controller.Document.Revision);
        Assert.False(_controller.Document.Session.IsDirty);
        OpenedMap opened = _store.Open(path);
        Assert.Equal(new MapTileLayer(4, 9), opened.Document[0, 0].GetLayer(0));
    }

    [Fact]
    public async Task Save_WithoutPath_DelegatesToSaveAs()
    {
        string path = MapPath("delegate.map");
        _dialogs.SavePickResult = path;

        await _controller.SaveAsync();

        Assert.Equal(1, _dialogs.SavePickShown);
        Assert.Equal(Path.GetFullPath(path), _controller.Document.Path);
        Assert.False(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task SaveAs_CanceledPicker_KeepsDirtyState()
    {
        EditorDocument before = _controller.Document;
        Paint(_controller.Document.Session, 0, 0, new MapTileLayer(1, 1));
        int stateChanges = 0;
        _controller.StateChanged += () => stateChanges++;

        _dialogs.SavePickResult = null;

        await _controller.SaveAsAsync();

        Assert.Same(before, _controller.Document);
        Assert.True(_controller.Document.Session.IsDirty);
        Assert.Null(_controller.Document.Path);
        Assert.Equal(0, stateChanges);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task SaveAs_PathOwnedByAnotherDocument_ReportsErrorAndKeepsDirtyState()
    {
        string ownedPath = MapPath("owned.map");
        EditorDocument initial = InitialDocument();
        var controller = new EditorDocumentController(_dialogs, _store, initial, (_, path) => path == ownedPath);
        Paint(initial.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.SavePickResult = ownedPath;

        await controller.SaveAsAsync();

        EditorDocument document = controller.Document;
        Assert.True(document.Session.IsDirty);
        Assert.Null(document.Path);
        Assert.Null(document.Revision);
        Assert.False(File.Exists(ownedPath));
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Save map", error.Title);
        Assert.Contains($"{ownedPath}: already open in another tab.", error.Message);
    }

    [Fact]
    public async Task SaveAs_UnownedPath_Writes()
    {
        string path = MapPath("unowned.map");
        EditorDocument initial = InitialDocument();
        var controller = new EditorDocumentController(_dialogs, _store, initial, (_, candidate) => candidate == MapPath("other.map"));
        Paint(initial.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.SavePickResult = path;

        await controller.SaveAsAsync();

        EditorDocument document = controller.Document;
        Assert.False(document.Session.IsDirty);
        Assert.Equal(Path.GetFullPath(path), document.Path);
        Assert.NotNull(document.Revision);
        OpenedMap opened = _store.Open(path);
        Assert.Equal(new MapTileLayer(1, 1), opened.Document[0, 0].GetLayer(0));
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task SaveAs_ValidationFailure_KeepsDirtyStatePathAndRevision()
    {
        string path = MapPath("invalid.map");
        _dialogs.SavePickResult = path;
        Paint(_controller.Document.Session, 0, 0, new MapTileLayer(short.MinValue - 1, 0));

        await _controller.SaveAsAsync();

        EditorDocument document = _controller.Document;
        Assert.True(document.Session.IsDirty);
        Assert.Null(document.Path);
        Assert.Null(document.Revision);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Save map", error.Title);
        Assert.Contains(path, error.Message);
        Assert.Contains((short.MinValue - 1).ToString(), error.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task SaveAs_UnwritableDestination_KeepsDirtyState()
    {
        string blocker = MapPath("blocker");
        File.WriteAllText(blocker, "x");
        string path = Path.Combine(blocker, "nested.map");
        _dialogs.SavePickResult = path;
        Paint(_controller.Document.Session, 0, 0, new MapTileLayer(1, 1));

        await _controller.SaveAsAsync();

        EditorDocument document = _controller.Document;
        Assert.True(document.Session.IsDirty);
        Assert.Null(document.Path);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Save map", error.Title);
        Assert.Contains(path, error.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Save_ExternalConflictCanceled_KeepsDirtyStateRevisionAndFile()
    {
        string path = MapPath("conflict.map");
        _dialogs.SavePickResult = path;
        await _controller.SaveAsAsync();
        MapFileRevision expected = _controller.Document.Revision!.Value;

        MapDocument external = MapDocument.Create();
        Paint(new MapEditSession(external), 0, 0, new MapTileLayer(9, 9));
        _store.Save(path, external);
        Paint(_controller.Document.Session, 1, 1, new MapTileLayer(3, 3));

        _dialogs.ExternalChangeResult = ExternalChangeChoice.Cancel;

        await _controller.SaveAsync();

        EditorDocument document = _controller.Document;
        Assert.True(document.Session.IsDirty);
        Assert.Equal(expected, document.Revision);
        Assert.Equal(Path.GetFullPath(path), document.Path);
        OpenedMap onDisk = _store.Open(path);
        Assert.Equal(new MapTileLayer(9, 9), onDisk.Document[0, 0].GetLayer(0));
        Assert.Equal(1, _dialogs.ExternalChangeShown);
    }

    [Fact]
    public async Task Save_ExternalConflictOverwrite_ReplacesFileAndMarksSaved()
    {
        string path = MapPath("overwrite.map");
        _dialogs.SavePickResult = path;
        await _controller.SaveAsAsync();

        MapDocument external = MapDocument.Create();
        Paint(new MapEditSession(external), 0, 0, new MapTileLayer(9, 9));
        _store.Save(path, external);
        Paint(_controller.Document.Session, 1, 1, new MapTileLayer(3, 3));

        _dialogs.ExternalChangeResult = ExternalChangeChoice.Overwrite;

        await _controller.SaveAsync();

        EditorDocument document = _controller.Document;
        Assert.False(document.Session.IsDirty);
        Assert.Equal(1, _dialogs.ExternalChangeShown);
        OpenedMap onDisk = _store.Open(path);
        Assert.Equal(new MapTileLayer(3, 3), onDisk.Document[1, 1].GetLayer(0));
        Assert.Equal(document.Revision, onDisk.Revision);
    }

    [Fact]
    public async Task Save_ExternalConflictSaveAs_SavesToNewDestinationLeavingOriginal()
    {
        string path = MapPath("conflict-as.map");
        string secondPath = MapPath("conflict-as-copy.map");
        _dialogs.SavePickResult = path;
        await _controller.SaveAsAsync();

        MapDocument external = MapDocument.Create();
        Paint(new MapEditSession(external), 0, 0, new MapTileLayer(9, 9));
        _store.Save(path, external);
        Paint(_controller.Document.Session, 1, 1, new MapTileLayer(3, 3));

        _dialogs.ExternalChangeResult = ExternalChangeChoice.SaveAs;
        _dialogs.SavePickResult = secondPath;

        await _controller.SaveAsync();

        Assert.Equal("conflict-as.map", _dialogs.LastSaveSuggestedName);
        EditorDocument document = _controller.Document;
        Assert.Equal(Path.GetFullPath(secondPath), document.Path);
        Assert.False(document.Session.IsDirty);
        OpenedMap original = _store.Open(path);
        Assert.Equal(new MapTileLayer(9, 9), original.Document[0, 0].GetLayer(0));
        OpenedMap copy = _store.Open(secondPath);
        Assert.Equal(new MapTileLayer(3, 3), copy.Document[1, 1].GetLayer(0));
    }

    [Fact]
    public async Task Save_ExternalConflictSaveAs_PathOwnedByAnotherDocument_ReportsErrorAndKeepsOriginal()
    {
        string path = MapPath("conflict-owned.map");
        string ownedPath = MapPath("owned-by-other.map");
        EditorDocument initial = InitialDocument();
        var controller = new EditorDocumentController(_dialogs, _store, initial, (_, candidate) => candidate == ownedPath);
        _dialogs.SavePickResult = path;
        await controller.SaveAsAsync();
        MapFileRevision expected = controller.Document.Revision!.Value;

        MapDocument external = MapDocument.Create();
        Paint(new MapEditSession(external), 0, 0, new MapTileLayer(9, 9));
        _store.Save(path, external);
        Paint(initial.Session, 1, 1, new MapTileLayer(3, 3));

        _dialogs.ExternalChangeResult = ExternalChangeChoice.SaveAs;
        _dialogs.SavePickResult = ownedPath;

        await controller.SaveAsync();

        EditorDocument document = controller.Document;
        Assert.True(document.Session.IsDirty);
        Assert.Equal(expected, document.Revision);
        Assert.Equal(Path.GetFullPath(path), document.Path);
        Assert.False(File.Exists(ownedPath));
        OpenedMap onDisk = _store.Open(path);
        Assert.Equal(new MapTileLayer(9, 9), onDisk.Document[0, 0].GetLayer(0));
        Assert.Equal(1, _dialogs.ExternalChangeShown);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Save map", error.Title);
        Assert.Contains($"{ownedPath}: already open in another tab.", error.Message);
    }

    [Fact]
    public async Task SaveAs_SamePathAfterExternalRewrite_StillGuardsExpectedRevision()
    {
        string path = MapPath("guard.map");
        _dialogs.SavePickResult = path;
        await _controller.SaveAsAsync();
        MapFileRevision expected = _controller.Document.Revision!.Value;

        MapDocument external = MapDocument.Create();
        Paint(new MapEditSession(external), 0, 0, new MapTileLayer(9, 9));
        _store.Save(path, external);
        Paint(_controller.Document.Session, 1, 1, new MapTileLayer(3, 3));

        var externalChangeChoices = new Queue<ExternalChangeChoice>();
        externalChangeChoices.Enqueue(ExternalChangeChoice.SaveAs);
        externalChangeChoices.Enqueue(ExternalChangeChoice.Cancel);
        _dialogs.ExternalChangeChoices = externalChangeChoices;
        _dialogs.SavePickResult = path;

        await _controller.SaveAsync();

        Assert.Equal(2, _dialogs.ExternalChangeShown);
        Assert.Equal(expected, _controller.Document.Revision);
        Assert.True(_controller.Document.Session.IsDirty);
        OpenedMap onDisk = _store.Open(path);
        Assert.Equal(new MapTileLayer(9, 9), onDisk.Document[0, 0].GetLayer(0));
    }

    [Fact]
    public async Task Save_AfterStrokeCompletion_SavesCompleteStrokeOnce()
    {
        MapEditSession session = _controller.Document.Session;
        session.SelectedTileLayer = new MapTileLayer(6, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.ContinueStroke(1, 1);
        Assert.True(session.CompleteStroke());

        string path = MapPath("stroke.map");
        _dialogs.SavePickResult = path;

        await _controller.SaveAsync();

        Assert.False(_controller.Document.Session.IsDirty);
        Assert.True(_controller.Document.Session.CanUndo);
        OpenedMap opened = _store.Open(path);
        Assert.Equal(new MapTileLayer(6, 1), opened.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(6, 1), opened.Document[1, 1].GetLayer(0));
    }

    [Fact]
    public async Task ConfirmClose_CleanDocument_ApprovesWithoutPrompt()
    {
        _dialogs.SavePickResult = MapPath("close-clean.map");
        await _controller.SaveAsAsync();

        Assert.True(await _controller.ConfirmCloseAsync());
        Assert.Equal(0, _dialogs.DirtyShown);
    }

    [Fact]
    public async Task ConfirmClose_DirtyAndCanceled_Rejects()
    {
        Paint(_controller.Document.Session, 0, 0, new MapTileLayer(1, 1));

        Assert.False(await _controller.ConfirmCloseAsync());
        Assert.Equal(1, _dialogs.DirtyShown);
        Assert.True(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task ConfirmClose_DirtyAndDiscarded_Approves()
    {
        Paint(_controller.Document.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.True(await _controller.ConfirmCloseAsync());
        Assert.True(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task ConfirmClose_DirtyAndSaveSucceeds_Approves()
    {
        Paint(_controller.Document.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.DirtyResult = DirtyChoice.Save;
        _dialogs.SavePickResult = MapPath("close-save.map");

        Assert.True(await _controller.ConfirmCloseAsync());
        Assert.False(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task ConfirmClose_DirtyAndSaveCanceled_Rejects()
    {
        Paint(_controller.Document.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.DirtyResult = DirtyChoice.Save;
        _dialogs.SavePickResult = null;

        Assert.False(await _controller.ConfirmCloseAsync());
        Assert.True(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task ConfirmClose_DialogFailure_PresentsErrorAndRemainsCancelableAgain()
    {
        Paint(_controller.Document.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.ShowDirtyException = new InvalidOperationException("dialog failed");

        Assert.False(await _controller.ConfirmCloseAsync());
        Assert.Equal("Close", Assert.Single(_dialogs.Errors).Title);

        _dialogs.ShowDirtyException = null;
        _dialogs.DirtyResult = DirtyChoice.Discard;
        Assert.True(await _controller.ConfirmCloseAsync());
    }

    [Fact]
    public async Task SaveAs_DialogFailure_PresentsLastResortErrorAndKeepsDocument()
    {
        EditorDocument before = _controller.Document;
        Paint(before.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.PickSaveException = new InvalidOperationException("dialog failed");

        await _controller.SaveAsAsync();

        Assert.Same(before, _controller.Document);
        Assert.True(before.Session.IsDirty);
        Assert.Null(before.Path);
        Assert.Null(before.Revision);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Save map", error.Title);
        Assert.Contains("dialog failed", error.Message);
    }

    private static readonly MapReference CloseMap10 = new(10, "Dungeon", "dungeon.map");
    private static readonly MapReference CloseMap20 = new(20, "Cave", "cave.map");
    private static readonly IReadOnlyList<MapReference> CloseMaps = new[] { CloseMap10, CloseMap20 };
    private static readonly string CloseSheetUrl = "https://docs.google.com/spreadsheets/d/abc123";
    private static readonly NpcAppearance CloseNpc1 = new(1, "Goose", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);

    private static RemoteGameData CloseData()
        => new(
            CloseMaps,
            new Dictionary<int, NpcAppearance> { [1] = CloseNpc1 },
            new List<RemoteRow<NpcSpawnRow>> { new(2, new NpcSpawnRow(1, 10, 3, 4)) },
            new List<RemoteRow<WarpRow>>());

    private static void MakeMapDirty(MapDocumentViewModel document)
    {
        document.Session.SelectedTileLayer = new MapTileLayer(1, 2);
        document.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(document.Session.CompleteStroke());
    }

    [Fact]
    public async Task TabClose_CleanSheetAndCleanMap_ClosesWithoutPrompts()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);

        Assert.True(await rig.Workspace.CloseAsync(document));

        Assert.DoesNotContain(document, rig.Workspace.Documents);
        Assert.Equal(0, rig.Dialogs.SheetDirtyShown);
        Assert.Equal(0, rig.Dialogs.DirtyShown);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task TabClose_DirtySheet_PushSucceeds_ClosesTabAndCleansSheet()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;

        Assert.True(await rig.Workspace.CloseAsync(document));

        Assert.DoesNotContain(document, rig.Workspace.Documents);
        Assert.Equal(1, rig.Dialogs.SheetDirtyShown);
        Assert.Equal(0, rig.Dialogs.DirtyShown);
        Assert.Equal(new[] { "ReadOwnedRowsAsync", "ReplaceOwnedRowsAsync" }, rig.Gateway.Calls.Select(call => call.Method));
    }

    [Fact]
    public async Task TabClose_DirtySheet_Discard_ClosesWithoutPushingOrSaving()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        EditorDocument editorDocument = document.Document;
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;

        Assert.True(await rig.Workspace.CloseAsync(document));

        Assert.DoesNotContain(document, rig.Workspace.Documents);
        Assert.Empty(rig.Gateway.Calls);
        Assert.Equal(0, rig.Dialogs.DirtyShown);
        Assert.Null(editorDocument.Path);
        Assert.Null(editorDocument.Revision);
    }

    [Fact]
    public async Task TabClose_DirtySheet_Cancel_KeepsTabOpenAndDirty()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Cancel;

        Assert.False(await rig.Workspace.CloseAsync(document));

        Assert.Contains(document, rig.Workspace.Documents);
        Assert.Same(document, rig.Workspace.ActiveDocument);
        Assert.True(document.GameData.IsDirty);
        Assert.Empty(rig.Gateway.Calls);
        Assert.Equal(0, rig.Dialogs.DirtyShown);
    }

    [Fact]
    public async Task TabClose_SheetPromptRunsBeforeMapPrompt()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        MakeMapDirty(document);
        var sheetGate = new TaskCompletionSource<SheetDirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Dialogs.SheetDirtyGate = sheetGate;

        Task<bool> closing = rig.Workspace.CloseAsync(document);
        await Until(() => rig.Dialogs.SheetDirtyShown == 1);

        Assert.Equal(0, rig.Dialogs.DirtyShown);
        sheetGate.SetResult(SheetDirtyChoice.Discard);
        rig.Dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.True(await closing);
        Assert.DoesNotContain(document, rig.Workspace.Documents);
        Assert.Equal(1, rig.Dialogs.DirtyShown);
    }

    [Fact]
    public async Task TabClose_PushSucceedsThenMapSaveCanceled_KeepsTabOpenSheetCleanMapDirty()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        rig.Dialogs.SavePickResult = rig.MapPath("baseline.map");
        await document.SaveAsAsync();
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        MakeMapDirty(document);
        string path = document.Document.Path!;
        MapFileRevision revision = document.Document.Revision!.Value;
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        rig.Dialogs.DirtyResult = DirtyChoice.Cancel;

        Assert.False(await rig.Workspace.CloseAsync(document));

        Assert.Contains(document, rig.Workspace.Documents);
        Assert.False(document.GameData.IsDirty);
        Assert.True(document.Session.IsDirty);
        Assert.Equal(path, document.Document.Path);
        Assert.Equal(revision, document.Document.Revision);
        Assert.Equal(1, rig.Dialogs.DirtyShown);
    }

    [Fact]
    public async Task TabClose_PushRejectedByValidation_KeepsTabOpenAndDirtyWithoutMapPrompt()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(999, 10, 5, 5));
        MakeMapDirty(document);
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        rig.Dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.False(await rig.Workspace.CloseAsync(document));

        Assert.Contains(document, rig.Workspace.Documents);
        Assert.True(document.GameData.IsDirty);
        Assert.True(document.Session.IsDirty);
        Assert.Equal(0, rig.Dialogs.DirtyShown);
        Assert.Single(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task TabClose_PushConflictCanceled_KeepsTabOpenAndDirtyWithoutMapPrompt()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        MakeMapDirty(document);
        rig.Gateway.EnqueueOwnedSpawns(new[] { new NpcSpawnRow(1, 10, 8, 8) });
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        rig.Dialogs.PushConflictResult = PushConflictChoice.Cancel;
        rig.Dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.False(await rig.Workspace.CloseAsync(document));

        Assert.Contains(document, rig.Workspace.Documents);
        Assert.True(document.GameData.IsDirty);
        Assert.True(document.Session.IsDirty);
        Assert.Equal(1, rig.Dialogs.PushConflictShown);
        Assert.Equal(0, rig.Dialogs.DirtyShown);
    }

    [Fact]
    public async Task TabClose_PushAmbiguous_KeepsTabOpenAndDirtyWithoutMapPrompt()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        MakeMapDirty(document);
        rig.Gateway.ReplaceFailure = new InvalidOperationException("socket reset");
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        rig.Dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.False(await rig.Workspace.CloseAsync(document));

        Assert.Contains(document, rig.Workspace.Documents);
        Assert.True(document.GameData.IsDirty);
        Assert.True(document.Session.IsDirty);
        Assert.Equal(0, rig.Dialogs.DirtyShown);
        Assert.Single(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task TabClose_DiscardSheet_DoesNotMarkMapSaved()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        rig.Dialogs.SavePickResult = rig.MapPath("saved.map");
        await document.SaveAsAsync();
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        MakeMapDirty(document);
        string path = document.Document.Path!;
        MapFileRevision revision = document.Document.Revision!.Value;
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;
        rig.Dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.True(await rig.Workspace.CloseAsync(document));

        Assert.DoesNotContain(document, rig.Workspace.Documents);
        Assert.Equal(path, document.Document.Path);
        Assert.Equal(revision, document.Document.Revision);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task TabClose_DiscardMap_DoesNotMarkSheetPushed()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        DocumentGameDataState sheetState = document.GameData!;
        MakeMapDirty(document);
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;
        rig.Dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.True(await rig.Workspace.CloseAsync(document));

        Assert.DoesNotContain(document, rig.Workspace.Documents);
        Assert.True(sheetState.IsDirty);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task CloseAll_MixedDirtyTabs_PromptsSheetFirstPerTabInWorkspaceOrder()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel first = await rig.PullAsync(rig.Workspace.ActiveDocument);
        first.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        MapDocumentViewModel second = await rig.NewDocumentAsync();
        MakeMapDirty(second);
        var sheetGate = new TaskCompletionSource<SheetDirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Dialogs.SheetDirtyGate = sheetGate;
        rig.Dialogs.DirtyResult = DirtyChoice.Discard;

        Task<bool> closing = rig.Workspace.CloseAllAsync();
        await Until(() => rig.Dialogs.SheetDirtyShown == 1);

        Assert.Same(first, rig.Workspace.ActiveDocument);
        Assert.Equal(0, rig.Dialogs.DirtyShown);
        sheetGate.SetResult(SheetDirtyChoice.Discard);

        Assert.True(await closing);
        Assert.Single(rig.Workspace.Documents);
        Assert.DoesNotContain(first, rig.Workspace.Documents);
        Assert.DoesNotContain(second, rig.Workspace.Documents);
        Assert.Equal(1, rig.Dialogs.SheetDirtyShown);
        Assert.Equal(1, rig.Dialogs.DirtyShown);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task MapSave_NeverPerformsPullOrPush()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        rig.Dialogs.SavePickResult = rig.MapPath("save1.map");
        await document.SaveAsAsync();
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        int callsBefore = rig.Gateway.Calls.Count;

        await document.SaveAsync();

        Assert.Equal(callsBefore, rig.Gateway.Calls.Count);
        Assert.True(document.GameData.IsDirty);
        Assert.False(document.Session.IsDirty);
    }

    [Fact]
    public async Task MapSaveAs_NeverPerformsPullOrPush()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Dialogs.SavePickResult = rig.MapPath("save2.map");

        await document.SaveAsAsync();

        Assert.Empty(rig.Gateway.Calls);
        Assert.True(document.GameData.IsDirty);
        Assert.False(document.Session.IsDirty);
    }

    [Fact]
    public async Task SheetPush_NeverTouchesMapFileStoreOrSavedState()
    {
        using var rig = new SheetCloseRig();
        MapDocumentViewModel document = await rig.PullAsync(rig.Workspace.ActiveDocument);
        rig.Dialogs.SavePickResult = rig.MapPath("pushed.map");
        await document.SaveAsAsync();
        string path = document.Document.Path!;
        MapFileRevision revision = document.Document.Revision!.Value;
        byte[] savedBytes = File.ReadAllBytes(path);
        document.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));

        Assert.True(await rig.Workspace.Commands.PushAsync(document));

        Assert.Equal(revision, document.Document.Revision);
        Assert.Equal(savedBytes, File.ReadAllBytes(path));
        Assert.False(document.Session.IsDirty);
        Assert.False(document.GameData.IsDirty);
        Assert.Equal(new[] { "ReadOwnedRowsAsync", "ReplaceOwnedRowsAsync" }, rig.Gateway.Calls.Select(call => call.Method));
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 1000 && !condition(); i++)
        {
            await Task.Delay(1);
        }
    }

    private sealed class SheetCloseRig : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-sheet-close-").FullName;

        public FakeEditorDialogs Dialogs { get; } = new();

        public MapFileStore Store { get; } = new();

        public RecordingGateway Gateway { get; } = new();

        public GameDataSyncCoordinator Coordinator { get; }

        public ScriptedConnectivity Connectivity { get; }

        public WorkspaceViewModel Workspace { get; }

        public SheetCloseRig()
        {
            Coordinator = new GameDataSyncCoordinator(Gateway, (_, _) => Task.CompletedTask);
            Connectivity = new ScriptedConnectivity { IsConnected = true, Coordinator = Coordinator };
            Workspace = new WorkspaceViewModel(Dialogs, Store, Connectivity);
        }

        public void Dispose() => Directory.Delete(_directory, true);

        public string MapPath(string name) => Path.Combine(_directory, name);

        public async Task<MapDocumentViewModel> PullAsync(MapDocumentViewModel document)
        {
            Dialogs.SpreadsheetUrlResult = CloseSheetUrl;
            Dialogs.MapConfirmationResult = CloseMap10;
            Assert.True(await Workspace.Commands.PullAsync(document));
            return document;
        }

        public async Task<MapDocumentViewModel> NewDocumentAsync()
        {
            Dialogs.NewMapResult = new NewMapRequest(10, 10);
            await Workspace.NewAsync();
            return Workspace.ActiveDocument;
        }
    }

    private sealed class RecordingGateway : IGameDataGateway
    {
        public sealed record Call(string Method);

        public List<Call> Calls { get; } = new();

        public Exception? ReplaceFailure;

        private IReadOnlyList<NpcSpawnRow> _ownedSpawns = new[] { new NpcSpawnRow(1, 10, 3, 4) };

        public RecordingGateway EnqueueOwnedSpawns(IReadOnlyList<NpcSpawnRow> spawns)
        {
            _ownedSpawns = spawns;
            return this;
        }

        public Task<IReadOnlyList<MapReference>> ReadMapsAsync(string spreadsheetId, CancellationToken cancellationToken)
            => Task.FromResult(CloseMaps);

        public Task<RemoteGameData> ReadGameDataAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken)
            => Task.FromResult(CloseData());

        public Task<RemoteOwnedRows> ReadOwnedRowsAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken)
        {
            Calls.Add(new Call("ReadOwnedRowsAsync"));
            return Task.FromResult(new RemoteOwnedRows(
                _ownedSpawns.Select((row, index) => new RemoteRow<NpcSpawnRow>(index + 2, row)).ToList(),
                new List<RemoteRow<WarpRow>>()));
        }

        public Task ReplaceOwnedRowsAsync(string spreadsheetId, ReplacementPlan spawnPlan, ReplacementPlan warpPlan, CancellationToken cancellationToken)
        {
            Calls.Add(new Call("ReplaceOwnedRowsAsync"));
            return ReplaceFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }
    }

    private sealed class ScriptedConnectivity : IGameDataConnectivity
    {
        public bool IsConnected { get; set; }

        public GameDataSyncCoordinator? Coordinator { get; set; }

        public SpreadsheetReference? RememberedSpreadsheet { get; set; }

        public bool TryRememberSpreadsheet(string? pastedUrl)
        {
            if (!SpreadsheetReferenceParser.TryParse(pastedUrl, out SpreadsheetReference reference))
            {
                return false;
            }

            RememberedSpreadsheet = reference;
            return true;
        }

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
