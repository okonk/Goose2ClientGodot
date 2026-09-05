using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Tests.Fakes;
using MapEditor.Core;
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
        string path = MapPath("save.bytes");
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
        string path = MapPath("delegate.bytes");
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
        string ownedPath = MapPath("owned.bytes");
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
        string path = MapPath("unowned.bytes");
        EditorDocument initial = InitialDocument();
        var controller = new EditorDocumentController(_dialogs, _store, initial, (_, candidate) => candidate == MapPath("other.bytes"));
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
        string path = MapPath("invalid.bytes");
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
        string path = Path.Combine(blocker, "nested.bytes");
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
        string path = MapPath("conflict.bytes");
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
        string path = MapPath("overwrite.bytes");
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
        string path = MapPath("conflict-as.bytes");
        string secondPath = MapPath("conflict-as-copy.bytes");
        _dialogs.SavePickResult = path;
        await _controller.SaveAsAsync();

        MapDocument external = MapDocument.Create();
        Paint(new MapEditSession(external), 0, 0, new MapTileLayer(9, 9));
        _store.Save(path, external);
        Paint(_controller.Document.Session, 1, 1, new MapTileLayer(3, 3));

        _dialogs.ExternalChangeResult = ExternalChangeChoice.SaveAs;
        _dialogs.SavePickResult = secondPath;

        await _controller.SaveAsync();

        Assert.Equal("conflict-as.bytes", _dialogs.LastSaveSuggestedName);
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
        string path = MapPath("conflict-owned.bytes");
        string ownedPath = MapPath("owned-by-other.bytes");
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
        string path = MapPath("guard.bytes");
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

        string path = MapPath("stroke.bytes");
        _dialogs.SavePickResult = path;

        await _controller.SaveAsync();

        Assert.False(_controller.Document.Session.IsDirty);
        Assert.True(_controller.Document.Session.CanUndo);
        OpenedMap opened = _store.Open(path);
        Assert.Equal(new MapTileLayer(6, 1), opened.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(6, 1), opened.Document[1, 1].GetLayer(0));
    }

    [Fact]
    public void UndoRedo_ToggleAvailabilityAndNotifyOncePerEffectiveChange()
    {
        Paint(_controller.Document.Session, 0, 0, new MapTileLayer(2, 5));
        int stateChanges = 0;
        _controller.StateChanged += () => stateChanges++;

        Assert.True(_controller.Undo());
        Assert.False(_controller.Document.Session.CanUndo);
        Assert.True(_controller.Document.Session.CanRedo);
        Assert.Equal(1, stateChanges);

        Assert.True(_controller.Redo());
        Assert.True(_controller.Document.Session.CanUndo);
        Assert.False(_controller.Document.Session.CanRedo);
        Assert.Equal(2, stateChanges);

        Assert.False(_controller.Redo());
        Assert.Equal(2, stateChanges);
    }

    [Fact]
    public void Undo_WithoutHistory_DoesNotNotify()
    {
        int stateChanges = 0;
        _controller.StateChanged += () => stateChanges++;

        Assert.False(_controller.Undo());
        Assert.Equal(0, stateChanges);
    }

    [Fact]
    public async Task ConfirmClose_CleanDocument_ApprovesWithoutPrompt()
    {
        _dialogs.SavePickResult = MapPath("close-clean.bytes");
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
        _dialogs.SavePickResult = MapPath("close-save.bytes");

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
}
