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
        _controller = new EditorDocumentController(_dialogs, _store);
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private string MapPath(string name) => Path.Combine(_directory, name);

    private void Paint(MapEditSession session, int x, int y, MapTileLayer layer)
    {
        session.SelectedTileLayer = layer;
        session.BeginStroke(MapEditTool.Pencil, x, y);
        Assert.True(session.CompleteStroke());
    }

    [Fact]
    public void Startup_IsDirtyUntitledDocumentWithEmptyHistory()
    {
        EditorDocument document = _controller.Document;

        Assert.True(document.Session.IsDirty);
        Assert.Null(document.Path);
        Assert.Null(document.Revision);
        Assert.Equal(MapDocument.DefaultWidth, document.Session.Document.Width);
        Assert.Equal(MapDocument.DefaultHeight, document.Session.Document.Height);
        Assert.False(document.Session.CanUndo);
        Assert.False(document.Session.CanRedo);
        Assert.False(document.Session.HasActiveStroke);
    }

    [Fact]
    public async Task New_ValidDimensions_PublishesNewDocumentOnce()
    {
        MapEditSession before = _controller.Document.Session;
        int stateChanges = 0;
        _controller.StateChanged += () => stateChanges++;

        _dialogs.NewMapResult = new NewMapRequest(50, 60);
        _dialogs.DirtyResult = DirtyChoice.Discard;

        await _controller.NewAsync();

        EditorDocument document = _controller.Document;
        Assert.NotSame(before, document.Session);
        Assert.Equal(50, document.Session.Document.Width);
        Assert.Equal(60, document.Session.Document.Height);
        Assert.True(document.Session.IsDirty);
        Assert.Null(document.Path);
        Assert.Null(document.Revision);
        Assert.Equal(1, _dialogs.DirtyShown);
        Assert.Equal(1, stateChanges);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1001, 100)]
    [InlineData(100, 0)]
    [InlineData(100, 1001)]
    public async Task New_InvalidDimensions_KeepsCurrentDocument(int width, int height)
    {
        EditorDocument before = _controller.Document;
        int stateChanges = 0;
        _controller.StateChanged += () => stateChanges++;

        _dialogs.NewMapResult = new NewMapRequest(width, height);

        await _controller.NewAsync();

        Assert.Same(before, _controller.Document);
        Assert.Equal(0, _dialogs.DirtyShown);
        Assert.Equal(0, stateChanges);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task New_Canceled_KeepsCurrentDocumentWithoutDirtyPrompt()
    {
        EditorDocument before = _controller.Document;

        _dialogs.NewMapResult = null;

        await _controller.NewAsync();

        Assert.Same(before, _controller.Document);
        Assert.Equal(0, _dialogs.DirtyShown);
    }

    [Fact]
    public async Task New_DirtyAndCanceled_KeepsCurrentDocument()
    {
        EditorDocument before = _controller.Document;

        _dialogs.NewMapResult = new NewMapRequest(10, 10);
        _dialogs.DirtyResult = DirtyChoice.Cancel;

        await _controller.NewAsync();

        Assert.Same(before, _controller.Document);
        Assert.Equal(1, _dialogs.DirtyShown);
        Assert.True(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task New_DirtyAndSave_SavesOldDocumentThenPublishesNewDocument()
    {
        string path = MapPath("old.bytes");
        Paint(_controller.Document.Session, 3, 4, new MapTileLayer(1, 2));

        _dialogs.NewMapResult = new NewMapRequest(10, 10);
        _dialogs.DirtyResult = DirtyChoice.Save;
        _dialogs.SavePickResult = path;
        int stateChanges = 0;
        _controller.StateChanged += () => stateChanges++;

        await _controller.NewAsync();

        EditorDocument document = _controller.Document;
        Assert.Equal(10, document.Session.Document.Width);
        Assert.Equal(10, document.Session.Document.Height);
        Assert.Equal("Untitled", _dialogs.LastSaveSuggestedName);
        OpenedMap saved = _store.Open(path);
        Assert.Equal(new MapTileLayer(1, 2), saved.Document[3, 4].GetLayer(0));
        Assert.Equal(2, stateChanges);
    }

    [Fact]
    public async Task New_DirtyAndSaveCanceled_AbortsNew()
    {
        EditorDocument before = _controller.Document;

        _dialogs.NewMapResult = new NewMapRequest(10, 10);
        _dialogs.DirtyResult = DirtyChoice.Save;
        _dialogs.SavePickResult = null;

        await _controller.NewAsync();

        Assert.Same(before, _controller.Document);
        Assert.True(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task Open_ValidFile_PublishesCleanDocumentWithPathAndRevision()
    {
        string path = MapPath("open.bytes");
        MapDocument source = MapDocument.Create(40, 30);
        Paint(new MapEditSession(source), 5, 6, new MapTileLayer(2, 7));
        MapFileRevision revision = _store.Save(path, source);

        MapEditSession before = _controller.Document.Session;
        int stateChanges = 0;
        _controller.StateChanged += () => stateChanges++;

        _dialogs.OpenPickResult = path;
        _dialogs.DirtyResult = DirtyChoice.Discard;

        await _controller.OpenAsync();

        EditorDocument document = _controller.Document;
        Assert.NotSame(before, document.Session);
        Assert.Equal(Path.GetFullPath(path), document.Path);
        Assert.Equal(revision, document.Revision);
        Assert.False(document.Session.IsDirty);
        Assert.False(document.Session.CanUndo);
        Assert.Equal(new MapTileLayer(2, 7), document.Session.Document[5, 6].GetLayer(0));
        Assert.Equal(1, stateChanges);
    }

    [Fact]
    public async Task Open_MissingFile_ReportsErrorAndKeepsCurrentDocument()
    {
        EditorDocument before = _controller.Document;

        _dialogs.OpenPickResult = MapPath("missing.bytes");
        _dialogs.DirtyResult = DirtyChoice.Discard;

        await _controller.OpenAsync();

        Assert.Same(before, _controller.Document);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Open map", error.Title);
        Assert.Contains("missing.bytes", error.Message);
    }

    [Fact]
    public async Task Open_MalformedFile_ReportsTypedFormatReasonAndKeepsCurrentDocument()
    {
        string path = MapPath("bad.bytes");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        EditorDocument before = _controller.Document;

        _dialogs.OpenPickResult = path;
        _dialogs.DirtyResult = DirtyChoice.Discard;

        await _controller.OpenAsync();

        Assert.Same(before, _controller.Document);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Open map", error.Title);
        Assert.Contains(path, error.Message);
        Assert.Contains("truncated header", error.Message);
    }

    [Fact]
    public async Task Open_CanceledPicker_KeepsCurrentDocument()
    {
        EditorDocument before = _controller.Document;

        _dialogs.OpenPickResult = null;

        await _controller.OpenAsync();

        Assert.Same(before, _controller.Document);
        Assert.Equal(0, _dialogs.DirtyShown);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task Open_DirtyAndCanceled_KeepsCurrentDocument()
    {
        EditorDocument before = _controller.Document;
        string path = MapPath("open2.bytes");
        _store.Save(path, MapDocument.Create(10, 10));

        _dialogs.OpenPickResult = path;
        _dialogs.DirtyResult = DirtyChoice.Cancel;

        await _controller.OpenAsync();

        Assert.Same(before, _controller.Document);
        Assert.Equal(1, _dialogs.DirtyShown);
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
    public async Task RequestClose_CleanDocument_ApprovesWithoutPrompt()
    {
        _dialogs.SavePickResult = MapPath("close-clean.bytes");
        await _controller.SaveAsAsync();

        Assert.True(await _controller.RequestCloseAsync());
        Assert.Equal(0, _dialogs.DirtyShown);
    }

    [Fact]
    public async Task RequestClose_DirtyAndCanceled_Rejects()
    {
        Assert.False(await _controller.RequestCloseAsync());
        Assert.Equal(1, _dialogs.DirtyShown);
        Assert.True(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task RequestClose_DirtyAndDiscarded_Approves()
    {
        _dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.True(await _controller.RequestCloseAsync());
        Assert.True(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task RequestClose_DirtyAndSaveSucceeds_Approves()
    {
        _dialogs.DirtyResult = DirtyChoice.Save;
        _dialogs.SavePickResult = MapPath("close-save.bytes");

        Assert.True(await _controller.RequestCloseAsync());
        Assert.False(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task RequestClose_DirtyAndSaveCanceled_Rejects()
    {
        _dialogs.DirtyResult = DirtyChoice.Save;
        _dialogs.SavePickResult = null;

        Assert.False(await _controller.RequestCloseAsync());
        Assert.True(_controller.Document.Session.IsDirty);
    }

    [Fact]
    public async Task RequestClose_SecondCallAfterApproval_DoesNotPromptAgain()
    {
        _dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.True(await _controller.RequestCloseAsync());
        int dirtyShown = _dialogs.DirtyShown;

        Assert.True(await _controller.RequestCloseAsync());
        Assert.Equal(dirtyShown, _dialogs.DirtyShown);
    }

    [Fact]
    public async Task RequestClose_DuplicateWhilePending_YieldsExactlyOnePrompt()
    {
        var gate = new TaskCompletionSource<DirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dialogs.DirtyGate = gate;

        Task<bool> first = _controller.RequestCloseAsync();
        Assert.False(await _controller.RequestCloseAsync());
        Assert.Equal(1, _dialogs.DirtyShown);

        gate.SetResult(DirtyChoice.Discard);
        Assert.True(await first);
    }

    [Fact]
    public async Task RequestClose_DialogFailure_PresentsErrorAndRemainsCancelableAgain()
    {
        _dialogs.ShowDirtyException = new InvalidOperationException("dialog failed");

        Assert.False(await _controller.RequestCloseAsync());
        Assert.Equal("Close", Assert.Single(_dialogs.Errors).Title);

        _dialogs.ShowDirtyException = null;
        _dialogs.DirtyResult = DirtyChoice.Discard;
        Assert.True(await _controller.RequestCloseAsync());
    }
}
