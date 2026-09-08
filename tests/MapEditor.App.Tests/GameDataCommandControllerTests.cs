using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapEditor.App.Connectivity;
using MapEditor.App.Dialogs;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;
using Xunit;

namespace MapEditor.App.Tests;

public class GameDataCommandControllerTests
{
    private static readonly MapReference Map10 = new(10, "Dungeon", "dungeon.bytes");
    private static readonly MapReference Map20 = new(20, "Cave", "cave.bytes");
    private static readonly MapReference Map30 = new(30, "Tower", "tower.bytes");
    private static readonly MapReference[] AllMaps = { Map10, Map20, Map30 };
    private static readonly string SheetUrl = "https://docs.google.com/spreadsheets/d/abc123";
    private static readonly NpcAppearance Npc1 = new(1, "Goose", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);

    private static RemoteGameData PullData(IReadOnlyList<MapReference> maps, IReadOnlyList<NpcSpawnRow>? spawns = null, IReadOnlyList<WarpRow>? warps = null)
    {
        spawns ??= new[] { new NpcSpawnRow(1, 10, 3, 4) };
        warps ??= Array.Empty<WarpRow>();
        return new RemoteGameData(
            maps,
            new Dictionary<int, NpcAppearance> { [1] = Npc1 },
            spawns.Select((row, index) => new RemoteRow<NpcSpawnRow>(index + 2, row)).ToList(),
            warps.Select((row, index) => new RemoteRow<WarpRow>(index + 2, row)).ToList());
    }

    private static RemoteOwnedRows OwnedRows(IReadOnlyList<NpcSpawnRow> spawns, IReadOnlyList<WarpRow>? warps = null)
        => new(
            spawns.Select((row, index) => new RemoteRow<NpcSpawnRow>(index + 2, row)).ToList(),
            (warps ?? Array.Empty<WarpRow>()).Select((row, index) => new RemoteRow<WarpRow>(index + 2, row)).ToList());

    [Fact]
    public async Task Connect_Success_ConnectsAndRaisesStateChanged()
    {
        using var rig = new Rig(connected: false);
        int stateChanges = 0;
        rig.Controller.StateChanged += () => stateChanges++;

        bool connected = await rig.Controller.ConnectAsync();

        Assert.True(connected);
        Assert.True(rig.Connectivity.IsConnected);
        Assert.Equal(1, stateChanges);
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Connect_Failure_PresentsErrorAndStaysDisconnected()
    {
        using var rig = new Rig(connected: false);
        rig.Connectivity.ConnectFailure = new InvalidOperationException("browser closed");

        bool connected = await rig.Controller.ConnectAsync();

        Assert.False(connected);
        Assert.False(rig.Connectivity.IsConnected);
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Equal("Connect Google", error.Title);
        Assert.Contains("browser closed", error.Message);
    }

    [Fact]
    public async Task Connect_Cancelled_StaysDisconnectedWithoutError()
    {
        using var rig = new Rig(connected: false);
        rig.Connectivity.ConnectFailure = new OperationCanceledException();

        bool connected = await rig.Controller.ConnectAsync();

        Assert.False(connected);
        Assert.False(rig.Connectivity.IsConnected);
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Connect_AlreadyConnected_DoesNotTouchConnection()
    {
        using var rig = new Rig(connected: true);

        bool connected = await rig.Controller.ConnectAsync();

        Assert.True(connected);
        Assert.Empty(rig.Connectivity.Calls);
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Connect_WithoutConnectivity_PresentsError()
    {
        var dialogs = new FakeEditorDialogs();
        var workspace = new WorkspaceViewModel(dialogs, new MapFileStore());

        bool connected = await workspace.Commands.ConnectAsync();

        Assert.False(connected);
        Assert.False(workspace.Commands.IsConnected);
        Assert.Single(dialogs.Errors);
    }

    [Fact]
    public async Task Pull_NotConnected_PresentsErrorAndPerformsNoApiWork()
    {
        using var rig = new Rig(connected: false);
        var document = rig.Workspace.ActiveDocument;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.False(pulled);
        Assert.Empty(rig.Gateway.Calls);
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Equal("Pull game data", error.Title);
        Assert.Null(document.GameData.Session);
    }

    [Fact]
    public async Task Pull_ValidUrl_SavesCanonicalFormAndLoadsCatalog()
    {
        using var rig = new Rig();
        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps));
        rig.Dialogs.SpreadsheetUrlResult = "https://docs.google.com/spreadsheets/d/abc123/edit#gid=0?usp=sharing";
        rig.Dialogs.MapConfirmationResult = Map10;
        var document = rig.Workspace.ActiveDocument;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.True(pulled);
        Assert.Equal("abc123", rig.Connectivity.RememberedSpreadsheet!.Value.Id);
        Assert.Equal("https://docs.google.com/spreadsheets/d/abc123", rig.Connectivity.RememberedSpreadsheet.Value.CanonicalUrl);
        Assert.Equal(new[] { "ReadMapsAsync", "ReadMapsAsync", "ReadGameDataAsync" }, rig.Gateway.Calls.Select(call => call.Method));
        Assert.Equal("abc123", document.GameData.Session.SpreadsheetId);
        Assert.Equal(10, document.GameData.Session.MapId);
        Assert.False(document.GameData.IsDirty);
    }

    [Fact]
    public async Task Pull_InvalidUrl_PresentsErrorSavesNothingAndPerformsNoApiWork()
    {
        using var rig = new Rig();
        rig.Dialogs.SpreadsheetUrlResult = "https://example.com/spreadsheet";

        bool pulled = await rig.Controller.PullAsync(rig.Workspace.ActiveDocument);

        Assert.False(pulled);
        Assert.Null(rig.Connectivity.RememberedSpreadsheet);
        Assert.Empty(rig.Gateway.Calls);
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Contains("not a valid", error.Message);
    }

    [Fact]
    public async Task Pull_CancelledAtCatalog_PreservesPreviousSession()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 6, 6));
        GameDataSyncSession before = document.GameData.Session;
        rig.Gateway.EnqueueMapsCancellation();
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.False(pulled);
        Assert.Same(before, document.GameData.Session);
        Assert.Equal(1, rig.Gateway.Calls.Skip(callsBefore).Count(call => call.Method == "ReadMapsAsync"));
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Pull_AuthenticationFailure_PresentsReauthorization()
    {
        using var rig = new Rig();
        rig.Gateway.EnqueueMapsFailure(GatewayFailureKind.Authentication);
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;

        bool pulled = await rig.Controller.PullAsync(rig.Workspace.ActiveDocument);

        Assert.False(pulled);
        Assert.Null(rig.Workspace.ActiveDocument.GameData.Session);
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Contains("Connect again", error.Message);
    }

    [Fact]
    public async Task Pull_PrefillsRememberedUrl()
    {
        using var rig = new Rig();
        rig.Connectivity.RememberedSpreadsheet = new SpreadsheetReference("abc123", "https://docs.google.com/spreadsheets/d/abc123");
        rig.Dialogs.SpreadsheetUrlResult = null;

        bool pulled = await rig.Controller.PullAsync(rig.Workspace.ActiveDocument);

        Assert.False(pulled);
        Assert.Equal(1, rig.Dialogs.SpreadsheetUrlShown);
        Assert.Equal("https://docs.google.com/spreadsheets/d/abc123", rig.Dialogs.LastSpreadsheetUrlPrefill);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task Pull_AutoSelectsTheUniqueFilenameMatchWithoutConfirming()
    {
        using var rig = new Rig();
        string path = rig.WriteMap("Dungeon.BYTES");
        var document = await rig.OpenDocumentAsync(path);
        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.True(pulled);
        Assert.Equal(0, rig.Dialogs.MapConfirmationShown);
        Assert.Equal(10, document.GameData.Session.MapId);
    }

    [Fact]
    public async Task Pull_AutoSelectsWhenOnlyTheExtensionDiffers()
    {
        using var rig = new Rig();
        string path = rig.WriteMap("Map10036.bytes");
        var document = await rig.OpenDocumentAsync(path);
        var maps = new[] { new MapReference(10001, "Minita", "Map10001.map"), new MapReference(10036, "Shops", "Map10036.map") };
        rig.Gateway.EnqueueMaps(maps).EnqueueGameData(PullData(maps));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.True(pulled);
        Assert.Equal(0, rig.Dialogs.MapConfirmationShown);
        Assert.Equal(10036, document.GameData.Session.MapId);
    }

    [Fact]
    public async Task Pull_ConfirmedMapDisagreeingWithTheFilenameKeepsTheDialogAndSuggestsTheConfirmedMap()
    {
        using var rig = new Rig();
        string path = rig.WriteMap("dungeon.bytes");
        var document = await rig.OpenDocumentAsync(path);
        await rig.PullDocumentAsync(document, Map10);
        var maps = new[] { Map10 with { MapFilename = "foo.bytes" }, Map20 with { MapFilename = "dungeon.bytes" }, Map30 };
        rig.Gateway.EnqueueMaps(maps).EnqueueGameData(PullData(maps));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;

        await rig.Controller.PullAsync(document);

        Assert.Equal(1, rig.Dialogs.MapConfirmationShown);
        Assert.Equal(maps[0], rig.Dialogs.LastMapConfirmationSuggested);
    }

    [Fact]
    public async Task Pull_ConfirmedMapMissingFromFetchedList_FallsBackToFilenameSuggestion()
    {
        using var rig = new Rig();
        string path = rig.WriteMap("dungeon.bytes");
        var document = await rig.OpenDocumentAsync(path);
        await rig.PullDocumentAsync(document, Map10);
        var maps = new[] { new MapReference(11, "Dungeon Copy", "dungeon.bytes"), Map20, Map30 };
        rig.Gateway.EnqueueMaps(maps).EnqueueGameData(PullData(maps));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;

        await rig.Controller.PullAsync(document);

        Assert.Equal(1, rig.Dialogs.MapConfirmationShown);
        Assert.Equal(maps[0], rig.Dialogs.LastMapConfirmationSuggested);
    }

    [Fact]
    public async Task Pull_UntitledDocumentHasNoSuggestion()
    {
        using var rig = new Rig();
        rig.Gateway.EnqueueMaps(AllMaps);
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;

        await rig.Controller.PullAsync(rig.Workspace.ActiveDocument);

        Assert.Null(rig.Dialogs.LastMapConfirmationSuggested);
    }

    [Fact]
    public async Task Pull_UnmatchedFilenameHasNoSuggestion()
    {
        using var rig = new Rig();
        string path = rig.WriteMap("other.bytes");
        var document = await rig.OpenDocumentAsync(path);
        rig.Gateway.EnqueueMaps(AllMaps);
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;

        await rig.Controller.PullAsync(document);

        Assert.Null(rig.Dialogs.LastMapConfirmationSuggested);
    }

    [Fact]
    public async Task Pull_DuplicateFilenameHasNoSuggestion()
    {
        using var rig = new Rig();
        string path = rig.WriteMap("dungeon.bytes");
        var document = await rig.OpenDocumentAsync(path);
        var maps = new[] { Map10, new MapReference(11, "Dungeon Copy", "dungeon.bytes") };
        rig.Gateway.EnqueueMaps(maps);
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;

        await rig.Controller.PullAsync(document);

        Assert.Null(rig.Dialogs.LastMapConfirmationSuggested);
    }

    [Fact]
    public async Task Pull_RequiresExplicitMapConfirmationEveryTime()
    {
        using var rig = new Rig();
        var document = rig.Workspace.ActiveDocument;
        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        Assert.True(await rig.Controller.PullAsync(document));

        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps));
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;
        Assert.True(await rig.Controller.PullAsync(document));

        Assert.Equal(2, rig.Dialogs.MapConfirmationShown);
        Assert.Equal(2, rig.Dialogs.SpreadsheetUrlShown);
    }

    [Fact]
    public async Task Pull_CancelledAtUrl_PreservesPreviousSession()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 6, 6));
        GameDataSyncSession before = document.GameData.Session;
        rig.Dialogs.SpreadsheetUrlResult = null;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.False(pulled);
        Assert.Same(before, document.GameData.Session);
        Assert.Equal(callsBefore, rig.Gateway.Calls.Count);
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Pull_CancelledAtMapConfirmation_PreservesPreviousSession()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 6, 6));
        GameDataSyncSession before = document.GameData.Session;
        rig.Gateway.EnqueueMaps(AllMaps);
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = null;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.False(pulled);
        Assert.Same(before, document.GameData.Session);
        Assert.Equal(new[] { "ReadMapsAsync" }, rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Pull_LateFailure_PreservesPreviousSessionByteForByte()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 6, 6));
        GameDataSyncSession before = document.GameData.Session;
        NpcSpawnRow[] beforeSpawns = before.Edits.Spawns.ToArray();
        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameDataFailure(GatewayFailureKind.Transport).EnqueueGameDataFailure(GatewayFailureKind.Transport).EnqueueGameDataFailure(GatewayFailureKind.Transport);
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.False(pulled);
        Assert.Same(before, document.GameData.Session);
        Assert.Equal(beforeSpawns, document.GameData.Session.Edits.Spawns);
        Assert.True(document.GameData.IsDirty);
        Assert.Equal(3, rig.Gateway.Calls.Skip(callsBefore).Count(call => call.Method == "ReadGameDataAsync"));
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Equal("Pull game data", error.Title);
    }

    [Fact]
    public async Task Pull_Dirty_PushChoice_SucceedsThenPulls()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Gateway
            .EnqueueMaps(AllMaps)
            .EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 3, 4) }))
            .EnqueueReplaceSuccess()
            .EnqueueGameData(PullData(AllMaps));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.True(pulled);
        Assert.Equal(
            new[] { "ReadMapsAsync", "ReadOwnedRowsAsync", "ReplaceOwnedRowsAsync", "ReadMapsAsync", "ReadGameDataAsync" },
            rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        Assert.False(document.GameData.IsDirty);
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Pull_Dirty_PushChoice_ValidationFailureAbortsPull()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 150, 150));
        GameDataSyncSession before = document.GameData.Session;
        rig.Gateway.EnqueueMaps(AllMaps);
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.False(pulled);
        Assert.Same(before, document.GameData.Session);
        Assert.True(document.GameData.IsDirty);
        Assert.Equal(new[] { "ReadMapsAsync" }, rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Contains("outside the current map bounds", error.Message);
    }

    [Fact]
    public async Task Pull_Dirty_PushChoice_ConflictCancelAbortsPull()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        GameDataSyncSession before = document.GameData.Session;
        rig.Gateway
            .EnqueueMaps(AllMaps)
            .EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 7, 7) }));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        rig.Dialogs.PushConflictResult = PushConflictChoice.Cancel;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.False(pulled);
        Assert.Same(before, document.GameData.Session);
        Assert.True(document.GameData.IsDirty);
        Assert.Equal(1, rig.Dialogs.PushConflictShown);
        Assert.DoesNotContain("ReplaceOwnedRowsAsync", rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        Assert.DoesNotContain("ReadGameDataAsync", rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Pull_Dirty_CancelChoice_PreservesPreviousSessionWithoutPulling()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 6, 6));
        GameDataSyncSession before = document.GameData.Session;
        NpcSpawnRow[] beforeSpawns = before.Edits.Spawns.ToArray();
        rig.Gateway.EnqueueMaps(AllMaps);
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Cancel;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pulled = await rig.Controller.PullAsync(document);

        Assert.False(pulled);
        Assert.Same(before, document.GameData.Session);
        Assert.Equal(beforeSpawns, document.GameData.Session.Edits.Spawns);
        Assert.True(document.GameData.IsDirty);
        Assert.Equal(1, rig.Dialogs.SheetDirtyShown);
        Assert.Equal(new[] { "ReadMapsAsync" }, rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Pull_Dirty_DiscardRequiresSeparateChoiceAfterFailedPush()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        GameDataSyncSession before = document.GameData.Session;

        rig.Gateway.EnqueueMaps(AllMaps).EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 7, 7) }));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        rig.Dialogs.PushConflictResult = PushConflictChoice.Cancel;
        Assert.False(await rig.Controller.PullAsync(document));
        Assert.Same(before, document.GameData.Session);

        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps));
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;
        Assert.True(await rig.Controller.PullAsync(document));

        Assert.NotSame(before, document.GameData.Session);
        Assert.False(document.GameData.IsDirty);
        Assert.Equal(2, rig.Dialogs.SheetDirtyShown);
    }

    [Fact]
    public async Task Push_Success_WritesRowsAndMarksPushed()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Gateway
            .EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 3, 4) }))
            .EnqueueReplaceSuccess();
        int callsBefore = rig.Gateway.Calls.Count;

        bool pushed = await rig.Controller.PushAsync(document);

        Assert.True(pushed);
        Assert.False(document.GameData.IsDirty);
        Assert.Equal(new[] { "ReadOwnedRowsAsync", "ReplaceOwnedRowsAsync" }, rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        Assert.Empty(rig.Dialogs.Errors);
        (string title, string message) info = Assert.Single(rig.Dialogs.Infos);
        Assert.Equal("Push game data", info.title);
        Assert.Equal("Successfully pushed 1 row to the sheet.", info.message);
    }

    [Fact]
    public async Task Push_NoChanges_SucceedsWithANoOpNotice()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        rig.Gateway.EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 3, 4) }));

        bool pushed = await rig.Controller.PushAsync(document);

        Assert.True(pushed);
        Assert.Empty(rig.Dialogs.Errors);
        (string title, string message) info = Assert.Single(rig.Dialogs.Infos);
        Assert.Equal("Push game data", info.title);
        Assert.Equal("Nothing to push — the sheet already matches this tab's game data.", info.message);
    }

    [Fact]
    public async Task Push_NoSession_PresentsError()
    {
        using var rig = new Rig();

        bool pushed = await rig.Controller.PushAsync(rig.Workspace.ActiveDocument);

        Assert.False(pushed);
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Contains("no pulled game data", error.Message);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task Push_ValidationRejected_PresentsIssuesAndStaysDirty()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(999, 10, 5, 5));
        int callsBeforePush = rig.Gateway.Calls.Count;

        bool pushed = await rig.Controller.PushAsync(document);

        Assert.False(pushed);
        Assert.True(document.GameData.IsDirty);
        Assert.Equal(callsBeforePush, rig.Gateway.Calls.Count);
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Contains("NPC '999'", error.Message);
    }

    [Fact]
    public async Task Push_Conflict_Overwrite_WritesLatestAndSucceeds()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Gateway
            .EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 7, 7) }))
            .EnqueueReplaceSuccess();
        rig.Dialogs.PushConflictResult = PushConflictChoice.Overwrite;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pushed = await rig.Controller.PushAsync(document);

        Assert.True(pushed);
        Assert.Equal(1, rig.Dialogs.PushConflictShown);
        Assert.Equal(new[] { "ReadOwnedRowsAsync", "ReplaceOwnedRowsAsync" }, rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        Assert.False(document.GameData.IsDirty);
    }

    [Fact]
    public async Task Push_Conflict_PullInstead_RepullsDiscardingLocal()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        GameDataSyncSession before = document.GameData.Session;
        rig.Gateway
            .EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 7, 7) }))
            .EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 7, 7) }))
            .EnqueueMaps(AllMaps)
            .EnqueueGameData(PullData(AllMaps));
        rig.Dialogs.PushConflictResult = PushConflictChoice.PullInstead;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pushed = await rig.Controller.PushAsync(document);

        Assert.True(pushed);
        Assert.Equal(
            new[] { "ReadOwnedRowsAsync", "ReadOwnedRowsAsync", "ReadMapsAsync", "ReadGameDataAsync" },
            rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        Assert.NotSame(before, document.GameData.Session);
        Assert.False(document.GameData.IsDirty);
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Push_Conflict_Cancel_KeepsDirtyWithoutWriting()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        GameDataSyncSession before = document.GameData.Session;
        rig.Gateway.EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 7, 7) }));
        rig.Dialogs.PushConflictResult = PushConflictChoice.Cancel;
        int callsBefore = rig.Gateway.Calls.Count;

        bool pushed = await rig.Controller.PushAsync(document);

        Assert.False(pushed);
        Assert.Same(before, document.GameData.Session);
        Assert.True(document.GameData.IsDirty);
        Assert.Equal(1, rig.Dialogs.PushConflictShown);
        Assert.Equal(new[] { "ReadOwnedRowsAsync" }, rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Push_AuthenticationFailure_PresentsReauthorizationAndKeepsEdits()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Gateway.EnqueueOwnedRowsFailure(GatewayFailureKind.Authentication);

        bool pushed = await rig.Controller.PushAsync(document);

        Assert.False(pushed);
        Assert.True(document.GameData.IsDirty);
        Assert.True(rig.Controller.CanPush(document));
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Contains("Connect again", error.Message);
    }

    [Fact]
    public async Task Push_Ambiguous_LatchesPushUntilPull()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Gateway
            .EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 3, 4) }))
            .EnqueueReplaceException(new InvalidOperationException("socket reset"));

        bool pushed = await rig.Controller.PushAsync(document);

        Assert.False(pushed);
        Assert.True(document.GameData.IsDirty);
        Assert.True(document.GameData.RequiresPull);
        Assert.False(rig.Controller.CanPush(document));
        Assert.Contains("Pull", Assert.Single(rig.Dialogs.Errors).Message);

        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;
        Assert.True(await rig.Controller.PullAsync(document));

        Assert.False(document.GameData.RequiresPull);
        Assert.True(rig.Controller.CanPush(document));
    }

    [Fact]
    public async Task Push_AfterAmbiguous_IsBlockedUntilPull()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Gateway
            .EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 3, 4) }))
            .EnqueueReplaceException(new InvalidOperationException("socket reset"));
        Assert.False(await rig.Controller.PushAsync(document));

        bool second = await rig.Controller.PushAsync(document);

        Assert.False(second);
        Assert.True(document.GameData.IsDirty);
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors, entry => entry.Message.Contains("complete a Pull"));
        Assert.Equal("Push game data", error.Title);
    }

    [Fact]
    public async Task Push_OpenDimensions_IncludeSameSpreadsheetConfirmedTabs()
    {
        using var rig = new Rig();
        string pathA = rig.WriteMap("dungeon.bytes", 20, 15);
        var a = await rig.OpenDocumentAsync(pathA);
        string pathB = rig.WriteMap("cave.bytes", 30, 40);
        var b = await rig.OpenDocumentAsync(pathB);

        var maps = new[] { Map10, Map20 };
        var warp = new WarpRow(10, 0, 0, 20, 35, 0);
        rig.Gateway.EnqueueMaps(maps).EnqueueGameData(PullData(maps, warps: new[] { warp }));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        Assert.True(await rig.Controller.PullAsync(a));

        rig.Gateway.EnqueueMaps(maps).EnqueueGameData(PullData(maps, spawns: Array.Empty<NpcSpawnRow>()));
        rig.Dialogs.MapConfirmationResult = Map20;
        Assert.True(await rig.Controller.PullAsync(b));

        rig.Gateway.EnqueueOwnedRows(OwnedRows(Array.Empty<NpcSpawnRow>(), new[] { warp }));
        bool pushed = await rig.Controller.PushAsync(a);

        Assert.False(pushed);
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Contains("outside map '20' bounds", error.Message);
    }

    [Fact]
    public async Task Push_OpenDimensions_ExcludeDifferentSpreadsheetAndUnconfirmedTabs()
    {
        using var rig = new Rig();
        string pathA = rig.WriteMap("dungeon.bytes", 20, 15);
        var a = await rig.OpenDocumentAsync(pathA);
        string pathB = rig.WriteMap("cave.bytes", 30, 40);
        var b = await rig.OpenDocumentAsync(pathB);
        string pathC = rig.WriteMap("tower.bytes", 5, 5);
        var c = await rig.OpenDocumentAsync(pathC);

        var maps = new[] { Map10, Map20, Map30 };
        var warp = new WarpRow(10, 0, 0, 20, 6, 0);
        rig.Gateway.EnqueueMaps(maps).EnqueueGameData(PullData(maps, warps: new[] { warp }));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        Assert.True(await rig.Controller.PullAsync(a));

        rig.Gateway.EnqueueMaps(maps).EnqueueGameData(PullData(maps, spawns: Array.Empty<NpcSpawnRow>()));
        rig.Dialogs.MapConfirmationResult = Map20;
        Assert.True(await rig.Controller.PullAsync(b));

        rig.Connectivity.RememberedSpreadsheet = new SpreadsheetReference("other999", "https://docs.google.com/spreadsheets/d/other999");
        rig.Gateway.EnqueueMaps(maps).EnqueueGameData(PullData(maps, spawns: Array.Empty<NpcSpawnRow>()));
        rig.Dialogs.SpreadsheetUrlResult = "https://docs.google.com/spreadsheets/d/other999";
        rig.Dialogs.MapConfirmationResult = Map20;
        Assert.True(await rig.Controller.PullAsync(c));

        rig.Gateway.EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 3, 4) }, new[] { warp })).EnqueueReplaceSuccess();
        a.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 1, 1));
        bool pushed = await rig.Controller.PushAsync(a);

        Assert.True(pushed);
        Assert.Equal(new[] { "ReadOwnedRowsAsync", "ReplaceOwnedRowsAsync" }, rig.Gateway.Calls.Skip(9).Select(call => call.Method));
    }

    [Fact]
    public async Task TwoTabs_ShareConnectivity_WithDistinctSyncState()
    {
        using var rig = new Rig();
        string pathA = rig.WriteMap("dungeon.bytes");
        var a = await rig.OpenDocumentAsync(pathA);
        string pathB = rig.WriteMap("cave.bytes");
        var b = await rig.OpenDocumentAsync(pathB);

        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        Assert.True(await rig.Controller.PullAsync(a));

        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps, spawns: Array.Empty<NpcSpawnRow>()));
        rig.Dialogs.MapConfirmationResult = Map20;
        Assert.True(await rig.Controller.PullAsync(b));

        Assert.Same(rig.Connectivity, rig.Workspace.Connectivity);
        Assert.NotSame(a.GameData, b.GameData);
        Assert.NotSame(a.GameData.Session, b.GameData.Session);
        Assert.Equal(10, a.GameData.Session.MapId);
        Assert.Equal(20, b.GameData.Session.MapId);
        Assert.Equal("abc123", a.GameData.Session.SpreadsheetId);
        Assert.Equal("abc123", b.GameData.Session.SpreadsheetId);
        Assert.Equal(6, rig.Gateway.Calls.Count);
    }

    [Fact]
    public async Task DisposedTab_StopsReceivingSharedCallbacks()
    {
        using var rig = new Rig(connected: false);
        var first = rig.Workspace.ActiveDocument;
        var second = await rig.NewDocumentAsync();
        int firstChanges = 0;
        int secondChanges = 0;
        first.GameData.Changed += () => firstChanges++;
        second.GameData.Changed += () => secondChanges++;

        Assert.True(await rig.Workspace.CloseAsync(first));
        Assert.True(await rig.Controller.ConnectAsync());

        Assert.Equal(0, firstChanges);
        Assert.Equal(1, secondChanges);
    }

    [Fact]
    public async Task Disconnect_NotConnected_IsNoOp()
    {
        using var rig = new Rig(connected: false);

        bool disconnected = await rig.Controller.DisconnectAsync();

        Assert.True(disconnected);
        Assert.Empty(rig.Connectivity.Calls);
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Disconnect_CleanTabs_DeletesToken()
    {
        using var rig = new Rig();

        bool disconnected = await rig.Controller.DisconnectAsync();

        Assert.True(disconnected);
        Assert.False(rig.Connectivity.IsConnected);
        Assert.Equal(new[] { "DisconnectAsync" }, rig.Connectivity.Calls);
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Disconnect_DirtyTab_Discard_DeletesTokenAndKeepsSession()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        GameDataSyncSession before = document.GameData.Session;
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;

        bool disconnected = await rig.Controller.DisconnectAsync();

        Assert.True(disconnected);
        Assert.Equal(new[] { "DisconnectAsync" }, rig.Connectivity.Calls);
        Assert.Same(before, document.GameData.Session);
        Assert.True(document.GameData.IsDirty);
    }

    [Fact]
    public async Task Disconnect_DirtyTab_Cancel_KeepsConnection()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Cancel;

        bool disconnected = await rig.Controller.DisconnectAsync();

        Assert.False(disconnected);
        Assert.True(rig.Connectivity.IsConnected);
        Assert.Empty(rig.Connectivity.Calls);
        Assert.True(document.GameData.IsDirty);
    }

    [Fact]
    public async Task Disconnect_DirtyTab_PushSucceeds_DeletesToken()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Gateway
            .EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 3, 4) }))
            .EnqueueReplaceSuccess();
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        int callsBefore = rig.Gateway.Calls.Count;

        bool disconnected = await rig.Controller.DisconnectAsync();

        Assert.True(disconnected);
        Assert.Equal(new[] { "ReadOwnedRowsAsync", "ReplaceOwnedRowsAsync" }, rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method));
        Assert.Equal(new[] { "DisconnectAsync" }, rig.Connectivity.Calls);
        Assert.False(document.GameData.IsDirty);
    }

    [Fact]
    public async Task Disconnect_DirtyTab_PushFails_KeepsConnectionAndSession()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(999, 10, 5, 5));
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;

        bool disconnected = await rig.Controller.DisconnectAsync();

        Assert.False(disconnected);
        Assert.True(rig.Connectivity.IsConnected);
        Assert.Empty(rig.Connectivity.Calls);
        Assert.True(document.GameData.IsDirty);
        Assert.Single(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Disconnect_MultipleDirtyTabs_ActivatesEachInWorkspaceOrder()
    {
        using var rig = new Rig();
        string pathA = rig.WriteMap("alpha.bytes");
        var first = await rig.OpenDocumentAsync(pathA);
        string pathB = rig.WriteMap("beta.bytes");
        var second = await rig.OpenDocumentAsync(pathB);
        string pathC = rig.WriteMap("gamma.bytes");
        var third = await rig.OpenDocumentAsync(pathC);

        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps));
        rig.Dialogs.SpreadsheetUrlResult = SheetUrl;
        rig.Dialogs.MapConfirmationResult = Map10;
        Assert.True(await rig.Controller.PullAsync(first));
        first.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));

        rig.Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps, spawns: Array.Empty<NpcSpawnRow>()));
        rig.Dialogs.MapConfirmationResult = Map20;
        Assert.True(await rig.Controller.PullAsync(third));
        third.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 20, 5, 5));

        var activations = new List<string>();
        rig.Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.ActiveDocument))
            {
                activations.Add(rig.Workspace.ActiveDocument.TabToolTip);
            }
        };
        var choices = new Queue<SheetDirtyChoice>();
        choices.Enqueue(SheetDirtyChoice.Discard);
        choices.Enqueue(SheetDirtyChoice.Discard);
        rig.Dialogs.SheetDirtyChoices = choices;

        bool disconnected = await rig.Controller.DisconnectAsync();

        Assert.True(disconnected);
        Assert.Equal(2, rig.Dialogs.SheetDirtyShown);
        Assert.Equal(new[] { pathA, pathC }, activations);
        Assert.Same(third, rig.Workspace.ActiveDocument);
        Assert.Equal(new[] { "DisconnectAsync" }, rig.Connectivity.Calls);
    }

    [Fact]
    public async Task Disconnect_TokenDeletionFails_PresentsErrorAndKeepsConnection()
    {
        using var rig = new Rig();
        rig.Connectivity.DisconnectFailure = new InvalidOperationException("token store locked");

        bool disconnected = await rig.Controller.DisconnectAsync();

        Assert.False(disconnected);
        Assert.True(rig.Connectivity.IsConnected);
        ErrorPresentation error = Assert.Single(rig.Dialogs.Errors);
        Assert.Equal("Disconnect Google", error.Title);
        Assert.Contains("token store locked", error.Message);
    }

    [Fact]
    public async Task Commands_SecondCallWhileOneIsInFlight_IsRejectedWithoutWork()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        GameDataSyncSession before = document.GameData.Session;
        var gate = new TaskCompletionSource<RemoteOwnedRows>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Gateway.ReadOwnedRowsGate = gate;
        int callsBefore = rig.Gateway.Calls.Count;
        int urlShownBefore = rig.Dialogs.SpreadsheetUrlShown;
        int dirtyShownBefore = rig.Dialogs.SheetDirtyShown;
        int conflictShownBefore = rig.Dialogs.PushConflictShown;

        Task<bool> push = rig.Controller.PushAsync(document);

        Assert.False(await rig.Controller.PullAsync(document));
        Assert.False(await rig.Controller.PushAsync(document));
        Assert.False(await rig.Controller.ConnectAsync());
        Assert.False(await rig.Controller.DisconnectAsync());

        Assert.Equal(callsBefore + 1, rig.Gateway.Calls.Count);
        Assert.Equal(urlShownBefore, rig.Dialogs.SpreadsheetUrlShown);
        Assert.Equal(dirtyShownBefore, rig.Dialogs.SheetDirtyShown);
        Assert.Equal(conflictShownBefore, rig.Dialogs.PushConflictShown);
        Assert.Empty(rig.Dialogs.Errors);
        Assert.Same(before, document.GameData.Session);
        Assert.True(document.GameData.IsDirty);

        rig.Gateway.EnqueueReplaceSuccess();
        gate.SetResult(OwnedRows(new[] { new NpcSpawnRow(1, 10, 3, 4) }));
        Assert.True(await push);
        Assert.False(document.GameData.IsDirty);
        Assert.True(await rig.Controller.DisconnectAsync());
    }

    [Fact]
    public async Task Disconnect_DrivesInternalPushWithoutTrippingTheGuard()
    {
        using var rig = new Rig();
        var document = await rig.PullDocumentAsync(rig.Workspace.ActiveDocument, Map10);
        document.GameData.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        rig.Gateway
            .EnqueueOwnedRows(OwnedRows(new[] { new NpcSpawnRow(1, 10, 3, 4) }))
            .EnqueueReplaceSuccess();
        rig.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        int callsBefore = rig.Gateway.Calls.Count;

        bool disconnected = await rig.Controller.DisconnectAsync();

        Assert.True(disconnected);
        Assert.False(rig.Connectivity.IsConnected);
        Assert.Equal(new[] { "ReadOwnedRowsAsync", "ReplaceOwnedRowsAsync" },
            rig.Gateway.Calls.Skip(callsBefore).Select(call => call.Method).ToArray());
        Assert.Equal(new[] { "DisconnectAsync" }, rig.Connectivity.Calls);
        Assert.False(document.GameData.IsDirty);
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public async Task Commands_RunNormallyAfterAPreviousCommandFails()
    {
        using var rig = new Rig(connected: false);
        rig.Connectivity.ConnectFailure = new InvalidOperationException("browser closed");

        Assert.False(await rig.Controller.ConnectAsync());
        Assert.False(await rig.Controller.ConnectAsync());

        Assert.Equal(2, rig.Connectivity.Calls.Count(call => call == "ConnectAsync"));
        Assert.Equal(2, rig.Dialogs.Errors.Count);
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-gdc-").FullName;

        public FakeEditorDialogs Dialogs { get; } = new();

        public MapFileStore Store { get; } = new();

        public ScriptedGateway Gateway { get; } = new();

        public GameDataSyncCoordinator Coordinator { get; }

        public ScriptedConnectivity Connectivity { get; }

        public WorkspaceViewModel Workspace { get; }

        public GameDataCommandController Controller => Workspace.Commands;

        public Rig(bool connected = true)
        {
            Coordinator = new GameDataSyncCoordinator(Gateway, (_, _) => Task.CompletedTask);
            Connectivity = new ScriptedConnectivity { IsConnected = connected, Coordinator = Coordinator };
            Workspace = new WorkspaceViewModel(Dialogs, Store, Connectivity);
        }

        public void Dispose() => Directory.Delete(_directory, true);

        public string WriteMap(string name, int width = 20, int height = 15)
        {
            string path = Path.Combine(_directory, name);
            Store.Save(path, MapDocument.Create(width, height));
            return path;
        }

        public async Task<MapDocumentViewModel> OpenDocumentAsync(string path)
        {
            Dialogs.OpenPickResult = path;
            await Workspace.OpenAsync();
            return Workspace.ActiveDocument;
        }

        public async Task<MapDocumentViewModel> NewDocumentAsync()
        {
            Dialogs.NewMapResult = new NewMapRequest(10, 10);
            await Workspace.NewAsync();
            return Workspace.ActiveDocument;
        }

        public async Task<MapDocumentViewModel> PullDocumentAsync(MapDocumentViewModel document, MapReference map)
        {
            Gateway.EnqueueMaps(AllMaps).EnqueueGameData(PullData(AllMaps));
            Dialogs.SpreadsheetUrlResult = SheetUrl;
            Dialogs.MapConfirmationResult = map;
            Assert.True(await Controller.PullAsync(document));
            return document;
        }
    }

    private sealed class ScriptedConnectivity : IGameDataConnectivity
    {
        public bool IsConnected { get; set; }

        public GameDataSyncCoordinator? Coordinator { get; set; }

        public SpreadsheetReference? RememberedSpreadsheet { get; set; }

        public List<string> Calls { get; } = new();

        public Exception? ConnectFailure;

        public Exception? DisconnectFailure;

        public bool TryRememberSpreadsheet(string? pastedUrl)
        {
            if (!SpreadsheetReferenceParser.TryParse(pastedUrl, out var reference))
            {
                return false;
            }

            RememberedSpreadsheet = reference;
            return true;
        }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            Calls.Add("ConnectAsync");
            if (ConnectFailure is { } failure)
            {
                return Task.FromException(failure);
            }

            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Calls.Add("DisconnectAsync");
            if (DisconnectFailure is { } failure)
            {
                return Task.FromException(failure);
            }

            IsConnected = false;
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedGateway : IGameDataGateway
    {
        public sealed record Call(string Method, string SpreadsheetId, int MapId);

        public List<Call> Calls { get; } = new();

        public TaskCompletionSource<RemoteOwnedRows>? ReadOwnedRowsGate;

        private IReadOnlyList<MapReference>? _maps;
        private Exception? _mapsFailure;
        private readonly Queue<object?> _gameData = new();
        private readonly Queue<object?> _ownedRows = new();
        private readonly Queue<object?> _replace = new();

        public ScriptedGateway EnqueueMaps(IReadOnlyList<MapReference> maps)
        {
            _maps = maps;
            _mapsFailure = null;
            return this;
        }

        public ScriptedGateway EnqueueMapsFailure(GatewayFailureKind kind)
        {
            _maps = null;
            _mapsFailure = new GameDataGatewayException(kind, "scripted", false, "scripted failure");
            return this;
        }

        public ScriptedGateway EnqueueMapsCancellation()
        {
            _maps = null;
            _mapsFailure = new OperationCanceledException();
            return this;
        }

        public ScriptedGateway EnqueueGameData(RemoteGameData data)
        {
            _gameData.Enqueue(data);
            return this;
        }

        public ScriptedGateway EnqueueGameDataFailure(GatewayFailureKind kind)
        {
            _gameData.Enqueue(new GameDataGatewayException(kind, "scripted", false, "scripted failure"));
            return this;
        }

        public ScriptedGateway EnqueueOwnedRows(RemoteOwnedRows rows)
        {
            _ownedRows.Enqueue(rows);
            return this;
        }

        public ScriptedGateway EnqueueOwnedRowsFailure(GatewayFailureKind kind)
        {
            _ownedRows.Enqueue(new GameDataGatewayException(kind, "scripted", false, "scripted failure"));
            return this;
        }

        public ScriptedGateway EnqueueReplaceSuccess()
        {
            _replace.Enqueue(null);
            return this;
        }

        public ScriptedGateway EnqueueReplaceException(Exception exception)
        {
            _replace.Enqueue(exception);
            return this;
        }

        public Task<IReadOnlyList<MapReference>> ReadMapsAsync(string spreadsheetId, CancellationToken cancellationToken)
        {
            Calls.Add(new Call("ReadMapsAsync", spreadsheetId, 0));
            if (_mapsFailure is { } failure)
            {
                return Task.FromException<IReadOnlyList<MapReference>>(failure);
            }

            if (_maps is null)
            {
                throw new InvalidOperationException("No scripted map catalog set.");
            }

            return Task.FromResult(_maps);
        }

        public Task<RemoteGameData> ReadGameDataAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken)
        {
            Calls.Add(new Call("ReadGameDataAsync", spreadsheetId, mapId));
            return Next<RemoteGameData>(_gameData);
        }

        public Task<RemoteOwnedRows> ReadOwnedRowsAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken)
        {
            Calls.Add(new Call("ReadOwnedRowsAsync", spreadsheetId, mapId));
            if (ReadOwnedRowsGate is { } gate)
            {
                return gate.Task;
            }

            return Next<RemoteOwnedRows>(_ownedRows);
        }

        public Task ReplaceOwnedRowsAsync(
            string spreadsheetId,
            ReplacementPlan spawnPlan,
            ReplacementPlan warpPlan,
            CancellationToken cancellationToken)
        {
            Calls.Add(new Call("ReplaceOwnedRowsAsync", spreadsheetId, 0));
            return Next<Task>(_replace);
        }

        private static Task<T> Next<T>(Queue<object?> queue)
        {
            if (queue.Count == 0)
            {
                throw new InvalidOperationException("No scripted result left for the call.");
            }

            var entry = queue.Dequeue();
            if (entry is Exception exception)
            {
                return Task.FromException<T>(exception);
            }

            return Task.FromResult((T)entry!);
        }
    }
}
