using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MapEditor.App.Connectivity;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Sync;
using Xunit;

namespace MapEditor.App.Tests;

public class WorkspaceViewModelTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-workspace-").FullName;
    private readonly FakeEditorDialogs _dialogs = new();
    private readonly MapFileStore _store = new();
    private readonly WorkspaceViewModel _workspace;

    public WorkspaceViewModelTests()
    {
        _workspace = new WorkspaceViewModel(_dialogs, _store);
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private string MapPath(string name) => Path.Combine(_directory, name);

    private string WriteMap(string name, int width = 20, int height = 15)
    {
        string path = MapPath(name);
        _store.Save(path, MapDocument.Create(width, height));
        return path;
    }

    private static void Paint(MapEditSession session, int x, int y, MapTileLayer layer)
    {
        session.SelectedTileLayer = layer;
        session.BeginStroke(MapEditTool.Pencil, x, y);
        Assert.True(session.CompleteStroke());
    }

    private static byte[] Header(short version, short editorVersion, int width, int height)
    {
        var bytes = new byte[MapCodec.HeaderSize];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, version);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), editorVersion);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), height);
        return bytes;
    }

    private async Task<MapDocumentViewModel> NewDocumentAsync(int width = 10, int height = 10)
    {
        _dialogs.NewMapResult = new NewMapRequest(width, height);
        await _workspace.NewAsync();
        return _workspace.ActiveDocument;
    }

    private async Task<MapDocumentViewModel> OpenDocumentAsync(string path)
    {
        _dialogs.OpenPickResult = path;
        await _workspace.OpenAsync();
        return _workspace.ActiveDocument;
    }

    private async Task<MapDocumentViewModel> NewDocumentAsync(WorkspaceViewModel workspace)
    {
        _dialogs.NewMapResult = new NewMapRequest(10, 10);
        await workspace.NewAsync();
        return workspace.ActiveDocument;
    }

    [Fact]
    public async Task New_EveryTabGetsItsOwnGameDataState()
    {
        MapDocumentViewModel a = await NewDocumentAsync();
        MapDocumentViewModel b = await NewDocumentAsync();

        Assert.NotNull(a.GameData);
        Assert.NotNull(b.GameData);
        Assert.NotSame(a.GameData, b.GameData);
        Assert.Null(a.GameData.Session);
        Assert.Null(b.GameData.Session);
        Assert.False(a.GameData.IsDirty);
        Assert.False(b.GameData.IsDirty);
    }

    [Fact]
    public async Task Open_EveryTabGetsItsOwnGameDataState()
    {
        string path = WriteMap("game-state.map");
        MapDocumentViewModel opened = await OpenDocumentAsync(path);
        MapDocumentViewModel fresh = await NewDocumentAsync();

        Assert.NotNull(opened.GameData);
        Assert.NotNull(fresh.GameData);
        Assert.NotSame(opened.GameData, fresh.GameData);
        Assert.Null(opened.GameData.Session);
    }

    [Fact]
    public async Task Tabs_ShareOneConnectivityAndController_WithDistinctGameDataState()
    {
        var scripted = new ScriptedConnectivity { IsConnected = true };
        var workspace = new WorkspaceViewModel(_dialogs, _store, scripted);
        MapDocumentViewModel a = await NewDocumentAsync(workspace);
        MapDocumentViewModel b = await NewDocumentAsync(workspace);

        Assert.Same(scripted, workspace.Connectivity);
        Assert.NotSame(a.GameData, b.GameData);
        Assert.Same(workspace.Commands, workspace.Commands);
        Assert.True(workspace.Commands.IsConnected);
    }

    [Fact]
    public async Task DisposedTab_StopsReceivingSharedControllerCallbacks()
    {
        var scripted = new ScriptedConnectivity();
        var workspace = new WorkspaceViewModel(_dialogs, _store, scripted);
        MapDocumentViewModel first = await NewDocumentAsync(workspace);
        MapDocumentViewModel second = await NewDocumentAsync(workspace);
        int firstChanges = 0;
        int secondChanges = 0;
        first.GameData.Changed += () => firstChanges++;
        second.GameData.Changed += () => secondChanges++;

        Assert.True(await workspace.CloseAsync(first));
        Assert.True(await workspace.Commands.ConnectAsync());

        Assert.Equal(0, firstChanges);
        Assert.Equal(1, secondChanges);
    }

    [Fact]
    public async Task New_AddsTabAndActivatesIt()
    {
        _dialogs.NewMapResult = new NewMapRequest(30, 40);

        await _workspace.NewAsync();

        Assert.Equal(2, _workspace.Documents.Count);
        MapDocumentViewModel active = _workspace.ActiveDocument;
        Assert.Equal(30, active.Session.Document.Width);
        Assert.Equal(40, active.Session.Document.Height);
        Assert.False(active.Session.IsDirty);
        Assert.Null(active.Document.Path);
    }

    [Fact]
    public async Task New_CancelledDialog_AddsNothing()
    {
        MapDocumentViewModel before = _workspace.ActiveDocument;
        _dialogs.NewMapResult = null;

        await _workspace.NewAsync();

        Assert.Single(_workspace.Documents);
        Assert.Same(before, _workspace.ActiveDocument);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task New_DoesNotPromptWhenCurrentIsDirty()
    {
        Paint(_workspace.ActiveDocument.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.NewMapResult = new NewMapRequest(10, 10);

        await _workspace.NewAsync();

        Assert.Equal(0, _dialogs.DirtyShown);
        Assert.Equal(2, _workspace.Documents.Count);
        Assert.NotSame(_workspace.ActiveDocument, _workspace.Documents[0]);
        Assert.False(_workspace.ActiveDocument.Session.IsDirty);
    }

    [Fact]
    public async Task Open_AddsTabAndActivatesIt()
    {
        string path = WriteMap("open.map", 25, 18);
        MapFileRevision revision = _store.Open(path).Revision;

        await OpenDocumentAsync(path);

        Assert.Equal(1, _workspace.Documents.Count);
        MapDocumentViewModel active = _workspace.ActiveDocument;
        Assert.Same(_workspace.Documents[0], active);
        Assert.Equal(Path.GetFullPath(path), active.Document.Path);
        Assert.Equal(revision, active.Document.Revision);
        Assert.Equal(25, active.Session.Document.Width);
        Assert.Equal(18, active.Session.Document.Height);
        Assert.False(active.Session.IsDirty);
    }

    [Fact]
    public async Task Open_AlreadyOpenPath_ActivatesExistingTabWithoutDuplicating()
    {
        string path = WriteMap("same.map");
        MapDocumentViewModel first = await OpenDocumentAsync(path);
        _workspace.Activate(_workspace.Documents[0]);

        await OpenDocumentAsync(path);

        Assert.Equal(1, _workspace.Documents.Count);
        Assert.Same(first, _workspace.ActiveDocument);
        Assert.Same(_workspace.Documents[0], first);
    }

    [Fact]
    public async Task Open_SingleDirtyBlank_KeepsTheBlank()
    {
        MapDocumentViewModel blank = _workspace.ActiveDocument;
        Paint(blank.Session, 0, 0, new MapTileLayer(1, 1));
        Assert.True(blank.Session.IsDirty);

        string path = WriteMap("dirty-blank.map");
        await OpenDocumentAsync(path);

        Assert.Equal(2, _workspace.Documents.Count);
        Assert.Same(blank, _workspace.Documents[0]);
        Assert.Same(_workspace.ActiveDocument, _workspace.Documents[1]);
    }

    [Fact]
    public async Task Open_MultipleTabs_KeepsTheBlank()
    {
        MapDocumentViewModel blank = _workspace.ActiveDocument;
        await NewDocumentAsync();

        string path = WriteMap("multi-tab.map");
        await OpenDocumentAsync(path);

        Assert.Equal(3, _workspace.Documents.Count);
        Assert.Same(blank, _workspace.Documents[0]);
    }

    [Fact]
    public async Task Open_SingleSavedTab_KeepsIt()
    {
        string pathA = WriteMap("a.map");
        await OpenDocumentAsync(pathA);
        Assert.Equal(1, _workspace.Documents.Count);

        string pathB = WriteMap("b.map");
        await OpenDocumentAsync(pathB);

        Assert.Equal(2, _workspace.Documents.Count);
        Assert.Equal(Path.GetFullPath(pathA), _workspace.Documents[0].Document.Path);
        Assert.Equal(Path.GetFullPath(pathB), _workspace.Documents[1].Document.Path);
    }

    [Fact]
    public async Task Open_InvalidFile_AddsNoTabAndReportsError()
    {
        string path = MapPath("bad-version.map");
        File.WriteAllBytes(path, Header(1, 99, 1, 1));
        int countBefore = _workspace.Documents.Count;

        await OpenDocumentAsync(path);

        Assert.Equal(countBefore, _workspace.Documents.Count);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Open map", error.Title);
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public async Task Open_MissingFile_ReportsErrorAndAddsNothing()
    {
        string path = MapPath("missing.map");

        await OpenDocumentAsync(path);

        Assert.Single(_workspace.Documents);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Open map", error.Title);
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public async Task Open_MalformedFile_ReportsTypedFormatReason()
    {
        string path = MapPath("truncated.map");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

        await OpenDocumentAsync(path);

        Assert.Single(_workspace.Documents);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Open map", error.Title);
        Assert.Equal($"{path}: {EditorDocumentController.DescribeFormatError(MapFormatError.TruncatedHeader)}.", error.Message);
    }

    [Fact]
    public async Task Open_CanceledPicker_AddsNothing()
    {
        MapDocumentViewModel before = _workspace.ActiveDocument;
        _dialogs.OpenPickResult = null;

        await _workspace.OpenAsync();

        Assert.Single(_workspace.Documents);
        Assert.Same(before, _workspace.ActiveDocument);
        Assert.Equal(0, _dialogs.DirtyShown);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task Open_DialogFailure_ReportsErrorAndAddsNothing()
    {
        _dialogs.PickOpenException = new InvalidOperationException("picker failed");

        await _workspace.OpenAsync();

        Assert.Single(_workspace.Documents);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Open map", error.Title);
        Assert.Contains("picker failed", error.Message);
    }

    [Fact]
    public async Task Open_InvalidDimensionsFile_ReportsValidationMessage()
    {
        string path = MapPath("zero-width.map");
        File.WriteAllBytes(path, Header(1, MapDocument.SupportedEditorVersion, 0, 10));

        await OpenDocumentAsync(path);

        Assert.Single(_workspace.Documents);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Open map", error.Title);
        Assert.Contains(path, error.Message);
        Assert.Contains("invalid dimensions", error.Message);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1001, 100)]
    [InlineData(100, 0)]
    [InlineData(100, 1001)]
    public async Task New_InvalidDimensions_AddsNothing(int width, int height)
    {
        MapDocumentViewModel before = _workspace.ActiveDocument;
        _dialogs.NewMapResult = new NewMapRequest(width, height);

        await _workspace.NewAsync();

        Assert.Single(_workspace.Documents);
        Assert.Same(before, _workspace.ActiveDocument);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task New_DialogFailure_ReportsErrorAndAddsNothing()
    {
        _dialogs.ShowNewMapException = new InvalidOperationException("new map failed");

        await _workspace.NewAsync();

        Assert.Single(_workspace.Documents);
        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("New map", error.Title);
        Assert.Contains("new map failed", error.Message);
    }

    [Fact]
    public async Task Close_CleanDocument_ActivatesRightNeighbour()
    {
        MapDocumentViewModel a = await NewDocumentAsync();
        MapDocumentViewModel b = await NewDocumentAsync();
        _workspace.Activate(a);

        bool closed = await _workspace.CloseAsync(a);

        Assert.True(closed);
        Assert.Equal(2, _workspace.Documents.Count);
        Assert.Same(b, _workspace.ActiveDocument);
    }

    [Fact]
    public async Task Close_LastInStrip_ActivatesNewLast()
    {
        MapDocumentViewModel a = await NewDocumentAsync();
        MapDocumentViewModel b = await NewDocumentAsync();

        bool closed = await _workspace.CloseAsync(b);

        Assert.True(closed);
        Assert.Equal(2, _workspace.Documents.Count);
        Assert.Same(a, _workspace.ActiveDocument);
    }

    [Fact]
    public async Task Close_OnlyDocument_LeavesFreshUntitled()
    {
        MapDocumentViewModel closed = _workspace.ActiveDocument;

        bool result = await _workspace.CloseAsync(closed);

        Assert.True(result);
        Assert.Single(_workspace.Documents);
        MapDocumentViewModel remaining = _workspace.ActiveDocument;
        Assert.NotSame(closed, remaining);
        Assert.False(remaining.Session.IsDirty);
        Assert.Null(remaining.Document.Path);
    }

    [Fact]
    public async Task Close_DirtyDocument_CancelKeepsIt()
    {
        MapDocumentViewModel document = _workspace.ActiveDocument;
        Paint(document.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.DirtyResult = DirtyChoice.Cancel;

        bool closed = await _workspace.CloseAsync(document);

        Assert.False(closed);
        Assert.Single(_workspace.Documents);
        Assert.Same(document, _workspace.ActiveDocument);
        Assert.True(document.Session.IsDirty);
    }

    [Fact]
    public async Task Close_DirtyDocument_DiscardDropsIt()
    {
        MapDocumentViewModel document = _workspace.ActiveDocument;
        Paint(document.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.DirtyResult = DirtyChoice.Discard;

        bool closed = await _workspace.CloseAsync(document);

        Assert.True(closed);
        Assert.Single(_workspace.Documents);
        Assert.DoesNotContain(document, _workspace.Documents);
        Assert.NotSame(document, _workspace.ActiveDocument);
        Assert.False(_workspace.ActiveDocument.Session.IsDirty);
    }

    [Fact]
    public async Task Close_DirtySaveThatFails_KeepsDocument()
    {
        MapDocumentViewModel document = _workspace.ActiveDocument;
        Paint(document.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.DirtyResult = DirtyChoice.Save;
        _dialogs.SavePickResult = null;

        bool closed = await _workspace.CloseAsync(document);

        Assert.False(closed);
        Assert.Contains(document, _workspace.Documents);
        Assert.True(document.Session.IsDirty);
    }

    [Fact]
    public async Task CloseAll_CancelOnSecond_StopsAndLeavesItActive()
    {
        MapDocumentViewModel second = await NewDocumentAsync();
        Paint(second.Session, 0, 0, new MapTileLayer(1, 1));
        await NewDocumentAsync();
        _dialogs.DirtyResult = DirtyChoice.Cancel;

        bool closed = await _workspace.CloseAllAsync();

        Assert.False(closed);
        Assert.Equal(2, _workspace.Documents.Count);
        Assert.Same(second, _workspace.ActiveDocument);
        Assert.Contains(second, _workspace.Documents);
        Assert.Equal(1, _dialogs.DirtyShown);
    }

    [Fact]
    public async Task CloseAll_AllApproved_LeavesFreshUntitled()
    {
        MapDocumentViewModel first = await NewDocumentAsync();
        MapDocumentViewModel second = await NewDocumentAsync();
        Paint(second.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.DirtyResult = DirtyChoice.Discard;

        bool closed = await _workspace.CloseAllAsync();

        Assert.True(closed);
        Assert.Single(_workspace.Documents);
        MapDocumentViewModel survivor = _workspace.ActiveDocument;
        Assert.NotSame(first, survivor);
        Assert.NotSame(second, survivor);
        Assert.DoesNotContain(first, _workspace.Documents);
        Assert.DoesNotContain(second, _workspace.Documents);
        Assert.False(survivor.Session.IsDirty);
        Assert.Null(survivor.Document.Path);
    }

    [Fact]
    public async Task Move_DoesNotChangeActiveDocument()
    {
        MapDocumentViewModel a = await NewDocumentAsync();
        await NewDocumentAsync();
        _workspace.Activate(a);

        _workspace.Move(0, 2);

        Assert.Same(a, _workspace.ActiveDocument);
        Assert.Same(a, _workspace.Documents[0]);
        Assert.Equal(3, _workspace.Documents.Count);
    }

    [Fact]
    public async Task Close_InactiveCleanTab_KeepsCurrentActiveDocument()
    {
        MapDocumentViewModel a = await NewDocumentAsync();
        MapDocumentViewModel b = await NewDocumentAsync();

        bool closed = await _workspace.CloseAsync(a);

        Assert.True(closed);
        Assert.Same(b, _workspace.ActiveDocument);
        Assert.Equal(2, _workspace.Documents.Count);
        Assert.Equal(0, _dialogs.DirtyShown);
    }

    [Fact]
    public async Task Close_InactiveDirtyTab_ActivatesItBeforePrompting()
    {
        MapDocumentViewModel dirty = await NewDocumentAsync();
        Paint(dirty.Session, 0, 0, new MapTileLayer(1, 1));
        await NewDocumentAsync();
        var gate = new TaskCompletionSource<DirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dialogs.DirtyGate = gate;

        Task<bool> closing = _workspace.CloseAsync(dirty);

        while (_dialogs.DirtyShown == 0)
        {
            await Task.Delay(10);
        }

        Assert.Same(dirty, _workspace.ActiveDocument);
        gate.SetResult(DirtyChoice.Cancel);
        Assert.False(await closing);
        Assert.Same(dirty, _workspace.ActiveDocument);
        Assert.Contains(dirty, _workspace.Documents);
    }

    [Fact]
    public async Task Close_DocumentNotInWorkspace_ReturnsFalseWithoutPrompting()
    {
        MapDocumentViewModel document = _workspace.ActiveDocument;
        Paint(document.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.DirtyResult = DirtyChoice.Discard;
        Assert.True(await _workspace.CloseAsync(document));

        bool second = await _workspace.CloseAsync(document);

        Assert.False(second);
        Assert.Equal(1, _dialogs.DirtyShown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Close_ActiveDocument_KeepsActiveInsideDocumentsAtEveryNotification(bool closeLast)
    {
        await NewDocumentAsync();
        MapDocumentViewModel last = await NewDocumentAsync();
        MapDocumentViewModel target = closeLast ? last : _workspace.Documents[1];
        _workspace.Activate(target);

        INotifyCollectionChanged documents = _workspace.Documents;
        documents.CollectionChanged += (_, _) =>
            Assert.Contains(_workspace.ActiveDocument, _workspace.Documents);

        bool closed = await _workspace.CloseAsync(target);

        Assert.True(closed);
        Assert.Contains(_workspace.ActiveDocument, _workspace.Documents);
    }

    [Fact]
    public async Task Close_OnlyDocument_KeepsActiveInsideDocumentsAtEveryNotification()
    {
        MapDocumentViewModel target = _workspace.ActiveDocument;

        INotifyCollectionChanged documents = _workspace.Documents;
        documents.CollectionChanged += (_, _) =>
            Assert.Contains(_workspace.ActiveDocument, _workspace.Documents);

        bool closed = await _workspace.CloseAsync(target);

        Assert.True(closed);
        Assert.Contains(_workspace.ActiveDocument, _workspace.Documents);
    }

    [Fact]
    public async Task Activate_RaisesPropertyChangedOnlyWhenChanged()
    {
        MapDocumentViewModel current = _workspace.ActiveDocument;
        int activeChanges = 0;
        _workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.ActiveDocument))
            {
                activeChanges++;
            }
        };

        _workspace.Activate(current);
        Assert.Equal(0, activeChanges);

        MapDocumentViewModel other = await NewDocumentAsync();
        _workspace.Activate(other);
        Assert.Equal(1, activeChanges);
        Assert.Same(other, _workspace.ActiveDocument);
    }

    [Fact]
    public void Activate_DocumentNotInWorkspace_Throws()
    {
        var external = new MapDocumentViewModel(
            new EditorDocumentController(_dialogs, _store, new EditorDocument(new MapEditSession(MapDocument.Create(), initiallyDirty: false), null, null)),
            _workspace.Clipboard);

        Assert.Throws<ArgumentException>(() => _workspace.Activate(external));
    }

    [Fact]
    public async Task SaveAs_ToAnotherDocumentsPath_LeavesBothDocumentsIntact()
    {
        string path = WriteMap("owned.map");
        MapDocumentViewModel opened = await OpenDocumentAsync(path);
        MapFileRevision revision = opened.Document.Revision!.Value;
        MapDocumentViewModel untitled = await NewDocumentAsync();
        Paint(untitled.Session, 0, 0, new MapTileLayer(1, 1));

        _dialogs.SavePickResult = path;
        await untitled.SaveAsAsync();

        ErrorPresentation error = Assert.Single(_dialogs.Errors);
        Assert.Equal("Save map", error.Title);
        Assert.Contains(Path.GetFullPath(path), error.Message);
        Assert.True(untitled.Session.IsDirty);
        Assert.Null(untitled.Document.Path);
        Assert.Equal(revision, opened.Document.Revision);
        Assert.Same(opened, _workspace.Documents[0]);
    }

    [Fact]
    public async Task SaveAs_AfterOwnerClosed_SucceedsAndWritesFile()
    {
        string path = WriteMap("released.map");
        MapDocumentViewModel owner = await OpenDocumentAsync(path);
        MapDocumentViewModel remaining = await NewDocumentAsync();

        Assert.True(await _workspace.CloseAsync(owner));
        Paint(remaining.Session, 0, 0, new MapTileLayer(1, 1));
        _dialogs.SavePickResult = path;
        await remaining.SaveAsAsync();

        Assert.Empty(_dialogs.Errors);
        Assert.Equal(Path.GetFullPath(path), remaining.Document.Path);
        Assert.False(remaining.Session.IsDirty);
        MapFileRevision revision = _store.Open(path).Revision;
        Assert.Equal(revision, remaining.Document.Revision);
    }

    private sealed class ScriptedConnectivity : IGameDataConnectivity
    {
        public bool IsConnected { get; set; }

        public GameDataSyncCoordinator? Coordinator { get; set; }

        public SpreadsheetReference? RememberedSpreadsheet { get; set; }

        public bool TryRememberSpreadsheet(string? pastedUrl) => false;

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }
    }
}
