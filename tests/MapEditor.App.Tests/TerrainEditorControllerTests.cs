using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using MapEditor.App.Dialogs;
using MapEditor.App.Rendering;
using MapEditor.App.Terrain;
using MapEditor.App.Tests.Fakes;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class TerrainEditorControllerTests : IDisposable
{
    private const string ManifestJson = """
        { "tileSize": 32, "sheets": { "1": { "10": [0, 0, 32, 32], "20": [32, 0, 32, 32] } } }
        """;

    private static readonly Guid GrassId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DirtId = new("22222222-2222-2222-2222-222222222222");

    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-terrain-ctrl-").FullName;
    private readonly FakeEditorDialogs _dialogs = new();
    private readonly FakeTerrainCatalogPublisher _publisher = new();
    private readonly FakeFileOperations _operations = new();
    private readonly TerrainCatalogFileStore _store;
    private readonly List<string> _timeline = new();

    private AssetContext? _context;
    private TerrainEditorController? _controller;

    public TerrainEditorControllerTests()
    {
        _store = new(_operations);
    }

    public void Dispose()
    {
        _controller?.Dispose();
        _context?.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private string SourcePath => Path.Combine(_directory, TerrainAssetCatalog.FileName);

    private TerrainEditorController CreateController(byte[]? terrainBytes)
    {
        File.WriteAllText(Path.Combine(_directory, "manifest.json"), ManifestJson);
        if (File.Exists(SourcePath))
        {
            File.Delete(SourcePath);
        }

        if (terrainBytes is not null)
        {
            File.WriteAllBytes(SourcePath, terrainBytes);
        }

        _operations.Clear();
        _operations.AddDirectory(_directory);
        if (terrainBytes is not null)
        {
            _operations.WriteFile(SourcePath, terrainBytes);
        }

        _context = AssetContext.Create(_directory, new CountingSpriteSheetLoader());
        _controller = new TerrainEditorController(_context, _store, _publisher, _dialogs);
        _publisher.Sink = _timeline.Add;
        _operations.Sink = _timeline.Add;
        return _controller;
    }

    private static byte[] Serialize(TerrainCatalog catalog)
        => Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(catalog));

    private static void MakeDirty(TerrainEditorSession session)
        => session.RenameTerrain(session.CurrentCatalog.Terrains[0].Id, "Renamed");

    private static TerrainCatalog CreateCatalog(params (Guid Id, string Name, int Sheet, int Graphic)[] entries)
    {
        var terrains = new List<TerrainDefinition>();
        var graphics = new List<TerrainGraphicDefinition>();
        foreach (var (id, name, sheet, graphic) in entries)
        {
            terrains.Add(new TerrainDefinition(id, name, null));
            graphics.Add(new TerrainGraphicDefinition(new TerrainGraphicReference(sheet, graphic), new TerrainPattern(Center: id)));
        }

        return new TerrainCatalog(terrains, graphics);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(5);
        }
    }

    [AvaloniaFact]
    public void Initialize_ValidLoad_UsesLoadedCatalogAndRevision()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var bytes = Serialize(catalog);
        var controller = CreateController(bytes);

        Assert.True(controller.IsTerrainFeaturesEnabled);
        Assert.Equal(TerrainFileRevision.FromBytes(bytes), controller.Revision);
        Assert.Equal(catalog, controller.Session.CurrentCatalog);
        Assert.False(controller.Session.IsDirty);
    }

    [AvaloniaFact]
    public void Initialize_MalformedLoad_StartsInRecoveryWithInvalidRevision()
    {
        var bytes = Encoding.UTF8.GetBytes("{ \"version\": 1,");
        var controller = CreateController(bytes);

        Assert.False(controller.IsTerrainFeaturesEnabled);
        Assert.Equal(TerrainFileRevision.FromBytes(bytes), controller.Revision);
        Assert.Empty(controller.Session.CurrentCatalog.Terrains);
        Assert.Empty(controller.Session.CurrentCatalog.Graphics);
        Assert.False(controller.Session.IsDirty);
    }

    [AvaloniaFact]
    public void Initialize_MissingFile_StartsEmptyWithMissingRevision()
    {
        var controller = CreateController(null);

        Assert.True(controller.IsTerrainFeaturesEnabled);
        Assert.Equal(TerrainFileRevision.Missing, controller.Revision);
        Assert.Empty(controller.Session.CurrentCatalog.Terrains);
        Assert.False(controller.Session.IsDirty);
    }

    [AvaloniaFact]
    public async Task Save_Clean_IsNoOp()
    {
        var bytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(bytes);
        var revision = controller.Revision;

        await controller.SaveAsync();

        Assert.Empty(_publisher.Events);
        Assert.Empty(_operations.Events);
        Assert.Equal(revision, controller.Revision);
        Assert.False(controller.Session.IsDirty);
        Assert.Empty(_dialogs.Errors);
        Assert.False(controller.Gate.IsBusy);
    }

    [AvaloniaFact]
    public async Task Save_PrepareFailure_PerformsNoFileIo()
    {
        var bytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(bytes);
        MakeDirty(controller.Session);
        _publisher.PrepareSaveFailure = new InvalidOperationException("reservation failed");

        await controller.SaveAsync();

        Assert.Empty(_operations.Events);
        Assert.Empty(_operations.DeletedPaths);
        Assert.Equal(new[] { "PrepareSaveFailed" }, _publisher.Events);
        Assert.Equal(0, _publisher.CommittedSaveCount);
        Assert.True(controller.Session.IsDirty);
        Assert.Equal(TerrainFileRevision.FromBytes(bytes), controller.Revision);
        Assert.Single(_dialogs.Errors);
        Assert.False(controller.Gate.IsBusy);
    }

    [AvaloniaFact]
    public async Task Save_Success_CommitsLeaseBeforeMarkingDraftSaved()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var bytes = Serialize(catalog);
        var controller = CreateController(bytes);
        MakeDirty(controller.Session);
        controller.Session.Changed += _ => _timeline.Add("sessionChanged");

        await controller.SaveAsync();

        Assert.Equal(9, _timeline.Count);
        Assert.Equal("PrepareSave", _timeline[0]);
        Assert.StartsWith("create:", _timeline[1]);
        Assert.Equal("write", _timeline[2]);
        Assert.Equal("flush", _timeline[3]);
        Assert.Equal("dispose", _timeline[4]);
        Assert.Equal($"read:{SourcePath}", _timeline[5]);
        Assert.Equal("move", _timeline[6]);
        Assert.Equal("CommitSave", _timeline[7]);
        Assert.Equal("sessionChanged", _timeline[8]);

        var prepared = _publisher.LastPreparedSave!;
        var committed = _publisher.LastCommittedSave!;
        Assert.Equal(prepared.OperationId, committed.OperationId);
        Assert.Same(prepared.Catalog, committed.Catalog);
        Assert.Same(prepared.Index, committed.Index);
        Assert.Equal(prepared.CanonicalBytes, _operations.ReadFileDirect(SourcePath));
        Assert.Equal(TerrainFileRevision.FromBytes(prepared.CanonicalBytes), controller.Revision);
        Assert.False(controller.Session.IsDirty);
        Assert.Equal(1, _publisher.CommittedSaveCount);
        Assert.True(controller.IsTerrainFeaturesEnabled);
    }

    [AvaloniaFact]
    public async Task Save_PendingFieldsCommittedBeforePrepare()
    {
        var controller = CreateController(Serialize(CreateCatalog((GrassId, "Grass", 1, 10))));
        var viewModel = controller.ViewModel;
        viewModel.SelectedTerrain = viewModel.Terrains[0];
        viewModel.Name = "Renamed Grass";

        await controller.SaveAsync();

        Assert.Equal("Renamed Grass", _publisher.LastPreparedSave!.Catalog.Terrains.Single().Name);
        Assert.Contains("Renamed Grass", Encoding.UTF8.GetString(_operations.ReadFileDirect(SourcePath)!));
    }

    [AvaloniaFact]
    public async Task Save_PendingValidationFailure_AbortsBeforePrepare()
    {
        var controller = CreateController(Serialize(CreateCatalog((GrassId, "Grass", 1, 10))));
        var viewModel = controller.ViewModel;
        viewModel.SelectedTerrain = viewModel.Terrains[0];
        viewModel.Name = string.Empty;

        await controller.SaveAsync();

        Assert.Empty(_publisher.Events);
        Assert.Empty(_operations.Events);
        Assert.True(viewModel.NameError is not null);
    }

    [AvaloniaFact]
    public async Task Save_InvalidCatalog_ShowsErrorAndPerformsNoIo()
    {
        var controller = CreateController(Serialize(CreateCatalog((GrassId, "Grass", 1, 10))));
        var session = controller.Session;
        session.BeginRegionStroke(GrassId);
        session.VisitRegion(new TerrainRegionKey(new TerrainGraphicReference(1, 99), TerrainPeer.Center));
        session.CompleteRegionStroke();

        await controller.SaveAsync();

        Assert.Empty(_operations.Events);
        Assert.Empty(_publisher.Events);
        Assert.Single(_dialogs.Errors);
        Assert.True(controller.Session.IsDirty);
    }

    [AvaloniaFact]
    public async Task Save_StoreFailure_DisposesPublicationAndPreservesDraftAndFile()
    {
        var bytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(bytes);
        MakeDirty(controller.Session);
        _operations.MoveFailure = new IOException("injected move failure");

        await controller.SaveAsync();

        Assert.Equal(new[] { "PrepareSave", "DisposeSavePublication" }, _publisher.Events);
        Assert.Equal(0, _publisher.CommittedSaveCount);
        Assert.Equal(1, _publisher.DisposedSavePublications);
        Assert.Equal(bytes, _operations.ReadFileDirect(SourcePath));
        Assert.True(controller.Session.IsDirty);
        Assert.Equal(TerrainFileRevision.FromBytes(bytes), controller.Revision);
        Assert.Single(_dialogs.Errors);
        Assert.False(controller.Gate.IsBusy);
    }

    [AvaloniaFact]
    public async Task Save_StoreFailure_DialogThrows_PreservesFileAndReleasesReservation()
    {
        var bytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(bytes);
        MakeDirty(controller.Session);
        _operations.MoveFailure = new IOException("injected move failure");
        _dialogs.ShowErrorException = new UnauthorizedAccessException("ui down");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.SaveAsync());

        Assert.Equal(new[] { "PrepareSave", "DisposeSavePublication" }, _publisher.Events);
        Assert.Equal(0, _publisher.CommittedSaveCount);
        Assert.Equal(bytes, _operations.ReadFileDirect(SourcePath));
        Assert.True(controller.Session.IsDirty);
        Assert.Equal(TerrainFileRevision.FromBytes(bytes), controller.Revision);
        Assert.False(controller.Gate.IsBusy);
    }

    [AvaloniaFact]
    public async Task Save_PublicationCommitFailure_LeavesDraftDirtyAndShowsError()
    {
        var bytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(bytes);
        MakeDirty(controller.Session);
        _publisher.CommitSaveFailure = new IOException("commit failed");

        await controller.SaveAsync();

        Assert.Equal(_publisher.LastPreparedSave!.CanonicalBytes, _operations.ReadFileDirect(SourcePath));
        Assert.Equal(new[] { "PrepareSave", "CommitSaveFailed", "DisposeSavePublication" }, _publisher.Events);
        Assert.Equal(0, _publisher.CommittedSaveCount);
        Assert.Equal(1, _publisher.DisposedSavePublications);
        Assert.True(controller.Session.IsDirty);
        Assert.Equal(TerrainFileRevision.FromBytes(bytes), controller.Revision);
        Assert.Single(_dialogs.Errors);
        Assert.False(controller.Gate.IsBusy);
    }

    [AvaloniaFact]
    public async Task Save_NotificationFailure_RecordedAndSaveStillSucceeds()
    {
        var bytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(bytes);
        MakeDirty(controller.Session);
        controller.Session.Changed += _ => throw new InvalidOperationException("observer down");

        await controller.SaveAsync();

        Assert.Single(controller.NotificationFailures);
        Assert.Equal(TerrainFileRevision.FromBytes(_publisher.LastPreparedSave!.CanonicalBytes), controller.Revision);
        Assert.False(controller.Session.IsDirty);
        Assert.Empty(_dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Overwrite_RereadsRevisionBeforeRetry()
    {
        var firstBytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(firstBytes);
        var externalBytes = Serialize(CreateCatalog((DirtId, "Dirt", 1, 20)));
        _operations.WriteFile(SourcePath, externalBytes);
        MakeDirty(controller.Session);
        _dialogs.ReplaceTerrainCatalogResult = TerrainExternalChangeChoice.Overwrite;

        await controller.SaveAsync();

        var read = $"read:{SourcePath}";
        var reads = _operations.Events.Where(e => e == read).ToList();
        var moveIndex = _operations.Events.IndexOf("move");
        Assert.True(moveIndex >= 0);
        Assert.True(reads.Count >= 3);
        Assert.True(_operations.Events.IndexOf(read, _operations.Events.IndexOf(read) + 1) < moveIndex);
        Assert.Equal(2, _publisher.Events.Count(e => e == "PrepareSave"));
        Assert.Equal(TerrainFileRevision.FromBytes(externalBytes), _publisher.LastPreparedSave!.ExpectedRevision);
        Assert.Equal(1, _publisher.CommittedSaveCount);
        Assert.Equal(_publisher.LastPreparedSave.CanonicalBytes, _operations.ReadFileDirect(SourcePath));
        Assert.Equal(TerrainFileRevision.FromBytes(_publisher.LastPreparedSave.CanonicalBytes), controller.Revision);
        Assert.False(controller.Session.IsDirty);
    }

    [AvaloniaFact]
    public async Task Save_Conflict_Cancel_PreservesDraftAndFile()
    {
        var firstBytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(firstBytes);
        var externalBytes = Serialize(CreateCatalog((DirtId, "Dirt", 1, 20)));
        _operations.WriteFile(SourcePath, externalBytes);
        MakeDirty(controller.Session);
        var originalSession = controller.Session;
        var revision = controller.Revision;
        _dialogs.ReplaceTerrainCatalogResult = TerrainExternalChangeChoice.Cancel;

        await controller.SaveAsync();

        Assert.Equal(new[] { "PrepareSave", "DisposeSavePublication" }, _publisher.Events);
        Assert.Equal(1, _dialogs.ReplaceTerrainCatalogShown);
        Assert.Equal(SourcePath, _dialogs.LastReplaceTerrainCatalogPath);
        Assert.Equal(externalBytes, _operations.ReadFileDirect(SourcePath));
        Assert.Same(originalSession, controller.Session);
        Assert.True(controller.Session.IsDirty);
        Assert.Equal(revision, controller.Revision);
        Assert.True(controller.IsTerrainFeaturesEnabled);
    }

    [AvaloniaFact]
    public async Task Save_Conflict_Reload_Valid_PublishesReplacementAndRebindsEditor()
    {
        var controller = CreateController(Serialize(CreateCatalog((GrassId, "Grass", 1, 10))));
        var external = CreateCatalog((DirtId, "Dirt", 1, 20));
        var externalBytes = Serialize(external);
        _operations.WriteFile(SourcePath, externalBytes);
        MakeDirty(controller.Session);
        var originalSession = controller.Session;
        var originalViewModel = controller.ViewModel;
        var stateChanged = 0;
        controller.StateChanged += () => stateChanged++;
        _dialogs.ReplaceTerrainCatalogResult = TerrainExternalChangeChoice.Reload;

        await controller.SaveAsync();

        Assert.Equal(new[] { "PrepareSave", "DisposeSavePublication", "PrepareLoaded", "CommitLoaded" }, _publisher.Events);
        Assert.Equal(TerrainLoadedPublicationKind.ValidReload, _publisher.LastLoadedKind);
        Assert.Equal(TerrainFileRevision.FromBytes(externalBytes), _publisher.LastLoadedResult!.Revision);
        Assert.NotSame(originalSession, controller.Session);
        Assert.NotSame(originalViewModel, controller.ViewModel);
        Assert.Equal(external, controller.Session.CurrentCatalog);
        Assert.Equal(TerrainFileRevision.FromBytes(externalBytes), controller.Revision);
        Assert.False(controller.Session.IsDirty);
        Assert.True(controller.IsTerrainFeaturesEnabled);
        Assert.Equal(1, stateChanged);
    }

    [AvaloniaFact]
    public async Task Save_Conflict_Reload_PublicationPrepareFailure_PreservesDraftAndReleasesReservation()
    {
        var firstBytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(firstBytes);
        var externalBytes = Serialize(CreateCatalog((DirtId, "Dirt", 1, 20)));
        _operations.WriteFile(SourcePath, externalBytes);
        MakeDirty(controller.Session);
        var originalSession = controller.Session;
        var revision = controller.Revision;
        _dialogs.ReplaceTerrainCatalogResult = TerrainExternalChangeChoice.Reload;
        _publisher.PrepareLoadedFailure = new InvalidOperationException("reservation failed");

        await controller.SaveAsync();

        Assert.Equal(new[] { "PrepareSave", "DisposeSavePublication", "PrepareLoadedFailed" }, _publisher.Events);
        Assert.Same(originalSession, controller.Session);
        Assert.True(controller.Session.IsDirty);
        Assert.Equal(revision, controller.Revision);
        Assert.True(controller.IsTerrainFeaturesEnabled);
        Assert.Equal(externalBytes, _operations.ReadFileDirect(SourcePath));
        Assert.Single(_dialogs.Errors);
        Assert.False(controller.Gate.IsBusy);
    }

    [AvaloniaFact]
    public async Task Save_Conflict_Reload_PublicationCommitFailure_PreservesDraftAndReleasesReservation()
    {
        var firstBytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(firstBytes);
        var externalBytes = Serialize(CreateCatalog((DirtId, "Dirt", 1, 20)));
        _operations.WriteFile(SourcePath, externalBytes);
        MakeDirty(controller.Session);
        var originalSession = controller.Session;
        var revision = controller.Revision;
        _dialogs.ReplaceTerrainCatalogResult = TerrainExternalChangeChoice.Reload;
        _publisher.CommitLoadedFailure = new IOException("commit failed");

        await controller.SaveAsync();

        Assert.Equal(new[] { "PrepareSave", "DisposeSavePublication", "PrepareLoaded", "CommitLoadedFailed", "DisposeLoadedPublication" }, _publisher.Events);
        Assert.Equal(0, _publisher.CommittedLoadedCount);
        Assert.Equal(1, _publisher.DisposedLoadedPublications);
        Assert.Same(originalSession, controller.Session);
        Assert.True(controller.Session.IsDirty);
        Assert.Equal(revision, controller.Revision);
        Assert.True(controller.IsTerrainFeaturesEnabled);
        Assert.Equal(externalBytes, _operations.ReadFileDirect(SourcePath));
        Assert.Single(_dialogs.Errors);
        Assert.False(controller.Gate.IsBusy);
    }

    [AvaloniaFact]
    public async Task Save_Conflict_Reload_Malformed_Confirmed_PublishesInvalidRevisionDisablesFeaturesAndRebinds()
    {
        var controller = CreateController(Serialize(CreateCatalog((GrassId, "Grass", 1, 10))));
        var malformed = Encoding.UTF8.GetBytes("{ \"version\": 1,");
        _operations.WriteFile(SourcePath, malformed);
        MakeDirty(controller.Session);
        var originalSession = controller.Session;
        _dialogs.ReplaceTerrainCatalogResult = TerrainExternalChangeChoice.Reload;
        _dialogs.ConfirmReplaceMalformedResult = true;

        await controller.SaveAsync();

        Assert.Equal(new[] { "PrepareSave", "DisposeSavePublication", "PrepareLoaded", "CommitLoaded" }, _publisher.Events);
        Assert.Equal(TerrainLoadedPublicationKind.ConfirmedMalformedReload, _publisher.LastLoadedKind);
        Assert.Equal(TerrainFileRevision.FromBytes(malformed), _publisher.LastLoadedResult!.Revision);
        Assert.NotSame(originalSession, controller.Session);
        Assert.Empty(controller.Session.CurrentCatalog.Terrains);
        Assert.Equal(TerrainFileRevision.FromBytes(malformed), controller.Revision);
        Assert.False(controller.IsTerrainFeaturesEnabled);
        Assert.False(controller.Session.IsDirty);
    }

    [AvaloniaFact]
    public async Task Save_Conflict_Reload_Malformed_Cancel_PreservesDraft()
    {
        var firstBytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(firstBytes);
        var malformed = Encoding.UTF8.GetBytes("{ \"version\": 1,");
        _operations.WriteFile(SourcePath, malformed);
        MakeDirty(controller.Session);
        var originalSession = controller.Session;
        var revision = controller.Revision;
        _dialogs.ReplaceTerrainCatalogResult = TerrainExternalChangeChoice.Reload;
        _dialogs.ConfirmReplaceMalformedResult = false;

        await controller.SaveAsync();

        Assert.Equal(new[] { "PrepareSave", "DisposeSavePublication" }, _publisher.Events);
        Assert.Equal(1, _dialogs.ConfirmReplaceMalformedShown);
        Assert.Equal(SourcePath, _dialogs.LastConfirmReplaceMalformedPath);
        Assert.Same(originalSession, controller.Session);
        Assert.True(controller.Session.IsDirty);
        Assert.Equal(revision, controller.Revision);
        Assert.True(controller.IsTerrainFeaturesEnabled);
        Assert.Equal(malformed, _operations.ReadFileDirect(SourcePath));
    }

    [AvaloniaFact]
    public async Task ReplaceWithEmptyCatalogAsync_Confirmed_RebindsEmptyDraftAndKeepsSaveExpectation()
    {
        var bytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(bytes);
        MakeDirty(controller.Session);
        var originalSession = controller.Session;
        var revision = controller.Revision;
        _dialogs.ConfirmReplaceMalformedResult = true;

        await controller.ReplaceWithEmptyCatalogAsync();

        Assert.NotSame(originalSession, controller.Session);
        Assert.Empty(controller.Session.CurrentCatalog.Terrains);
        Assert.False(controller.Session.IsDirty);
        Assert.False(controller.IsTerrainFeaturesEnabled);
        Assert.Equal(revision, controller.Revision);
        Assert.Empty(_operations.Events);
        Assert.Empty(_publisher.Events);

        var recoverySession = controller.Session;
        var recoveryTerrain = recoverySession.AddTerrain();
        recoverySession.BeginRegionStroke(recoveryTerrain);
        recoverySession.VisitRegion(new TerrainRegionKey(new TerrainGraphicReference(1, 10), TerrainPeer.Center));
        recoverySession.CompleteRegionStroke();

        await controller.SaveAsync();

        Assert.Equal(revision, _publisher.LastPreparedSave!.ExpectedRevision);
        Assert.Equal(1, _publisher.CommittedSaveCount);
        Assert.False(controller.Session.IsDirty);
    }

    [AvaloniaFact]
    public async Task ReplaceWithEmptyCatalogAsync_Cancel_ChangesNothing()
    {
        var bytes = Serialize(CreateCatalog((GrassId, "Grass", 1, 10)));
        var controller = CreateController(bytes);
        MakeDirty(controller.Session);
        var originalSession = controller.Session;
        var revision = controller.Revision;
        _dialogs.ConfirmReplaceMalformedResult = false;

        await controller.ReplaceWithEmptyCatalogAsync();

        Assert.Equal(1, _dialogs.ConfirmReplaceMalformedShown);
        Assert.Same(originalSession, controller.Session);
        Assert.True(controller.Session.IsDirty);
        Assert.True(controller.IsTerrainFeaturesEnabled);
        Assert.Equal(revision, controller.Revision);
        Assert.Empty(_operations.Events);
        Assert.Empty(_publisher.Events);
    }

    [AvaloniaFact]
    public async Task SaveWithLeaseAsync_ForeignOrStaleLease_Throws()
    {
        var controller = CreateController(Serialize(CreateCatalog((GrassId, "Grass", 1, 10))));
        var foreignGate = new TerrainOperationGate();
        using var foreignLease = await foreignGate.AcquireAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SaveWithLeaseAsync(foreignLease));

        var ownLease = await controller.Gate.AcquireAsync();
        ownLease.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SaveWithLeaseAsync(ownLease));
    }

    [AvaloniaFact]
    public async Task CloseDuringConflict_WaitsUntilConflictIsResolved()
    {
        var controller = CreateController(Serialize(CreateCatalog((GrassId, "Grass", 1, 10))));
        _operations.WriteFile(SourcePath, Serialize(CreateCatalog((DirtId, "Dirt", 1, 20))));
        MakeDirty(controller.Session);
        var gate = new TaskCompletionSource<TerrainExternalChangeChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dialogs.ReplaceTerrainCatalogGate = gate;

        var saveTask = Task.Run(() => controller.SaveAsync());
        await WaitUntilAsync(() => _dialogs.ReplaceTerrainCatalogShown == 1);

        var closeAcquire = controller.Gate.AcquireAsync();
        Assert.False(closeAcquire.IsCompleted);

        gate.SetResult(TerrainExternalChangeChoice.Cancel);
        await saveTask;
        var closeLease = await closeAcquire;
        closeLease.Dispose();
        Assert.False(controller.Gate.IsBusy);
        Assert.True(controller.Session.IsDirty);
    }

    [AvaloniaFact]
    public async Task RootSwitchDuringSave_WaitsUntilSaveCompletes()
    {
        var controller = CreateController(Serialize(CreateCatalog((GrassId, "Grass", 1, 10))));
        MakeDirty(controller.Session);
        var blocker = new ManualResetEventSlim(false);
        _operations.MoveBlocker = blocker;

        var saveTask = Task.Run(() => controller.SaveAsync());
        await WaitUntilAsync(() => _operations.MoveCalls == 1);

        var rootAcquire = controller.Gate.AcquireAsync();
        Assert.False(rootAcquire.IsCompleted);

        blocker.Set();
        await saveTask;
        var rootLease = await rootAcquire;
        rootLease.Dispose();
        Assert.False(controller.Gate.IsBusy);
        Assert.Equal(1, _publisher.CommittedSaveCount);
        Assert.False(controller.Session.IsDirty);
    }

    [AvaloniaFact]
    public async Task DisposeWithLiveLease_KeepsReservationAndRejectsNewAcquires()
    {
        var gate = new TerrainOperationGate();
        using var lease = await gate.AcquireAsync();
        var completion = gate.Completion;
        Assert.False(completion.IsCompleted);
        var pending = gate.AcquireAsync();
        Assert.False(pending.IsCompleted);

        gate.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => gate.AcquireAsync());
        lease.Dispose();

        await completion;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
    }

    private sealed class FakeFileOperations : ITerrainCatalogFileOperations
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

        public List<string> Events { get; } = new();
        public List<string> DeletedPaths { get; } = new();
        public int MoveCalls { get; private set; }
        public Action<string>? Sink;
        public Exception? CreateFailure;
        public Exception? WriteFailure;
        public Exception? FlushFailure;
        public Exception? DisposeFailure;
        public Exception? MoveFailure;
        public Exception? DeleteFailure;
        public ManualResetEventSlim? MoveBlocker;

        public void Clear()
        {
            _files.Clear();
            _directories.Clear();
            Events.Clear();
            DeletedPaths.Clear();
            MoveCalls = 0;
            CreateFailure = null;
            WriteFailure = null;
            FlushFailure = null;
            DisposeFailure = null;
            MoveFailure = null;
            DeleteFailure = null;
            MoveBlocker = null;
        }

        public void AddDirectory(string path)
        {
            _directories.Add(path);
        }

        public void WriteFile(string path, byte[] bytes)
        {
            _files[path] = bytes;
        }

        public byte[]? ReadFileDirect(string path)
            => _files.TryGetValue(path, out var bytes) ? bytes : null;

        public void CommitFile(string path, byte[] bytes)
        {
            _files[path] = bytes;
        }

        internal void Record(string name)
        {
            Events.Add(name);
            Sink?.Invoke(name);
        }

        public bool DirectoryExists(string path)
            => _directories.Contains(path);

        public bool FileExists(string path)
            => _files.ContainsKey(path);

        public byte[] ReadFile(string path)
        {
            Record($"read:{path}");
            return _files[path];
        }

        public Stream CreateNew(string path)
        {
            Record($"create:{path}");
            if (CreateFailure is { } failure)
            {
                throw failure;
            }

            _files[path] = Array.Empty<byte>();
            return new RecordingStream(this, path);
        }

        public void FlushToDisk(Stream stream)
        {
            Record("flush");
            if (FlushFailure is { } failure)
            {
                throw failure;
            }
        }

        public void AtomicOverwrite(string source, string destination)
        {
            Record("move");
            MoveCalls++;
            MoveBlocker?.Wait();
            if (MoveFailure is { } failure)
            {
                throw failure;
            }

            _files[destination] = _files[source];
            _files.Remove(source);
        }

        public void Delete(string path)
        {
            DeletedPaths.Add(path);
            if (DeleteFailure is { } failure)
            {
                throw failure;
            }

            _files.Remove(path);
        }
    }

    private sealed class RecordingStream : Stream
    {
        private readonly FakeFileOperations _operations;
        private readonly string _path;
        private readonly MemoryStream _buffer = new();

        internal RecordingStream(FakeFileOperations operations, string path)
        {
            _operations = operations;
            _path = path;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_operations.WriteFailure is { } failure)
            {
                throw failure;
            }

            _operations.Record("write");
            _buffer.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_operations.DisposeFailure is { } failure)
                {
                    throw failure;
                }

                _operations.Record("dispose");
                _operations.CommitFile(_path, _buffer.ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
