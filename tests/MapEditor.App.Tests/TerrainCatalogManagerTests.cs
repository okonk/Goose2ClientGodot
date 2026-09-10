using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MapEditor.App.Dialogs;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Terrain;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.App.Tests;

public sealed class TerrainCatalogManagerTests
{
    [Fact]
    public void ManagerConstructor_CapturesExactCurrentContextDirectoryManifestAndSource()
    {
        using Harness h = new();
        AssetContext source = h.Controller.Current;
        object? capturedManifest = typeof(TerrainCatalogManager).GetField("_manifest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(h.Manager);

        Assert.Same(source, h.Manager.SourceContext);
        Assert.Same(source.Cache.Manifest, capturedManifest);
        Assert.Equal(TerrainCatalogJson.Serialize(source.Terrain.Source!), TerrainCatalogJson.Serialize(h.Manager.ViewModel.Build()));
        Assert.Equal(Path.GetFullPath(h.Fixture.AssetDirectory), h.Manager.AssetDirectory);
    }

    [Fact] public void Save_CleanReturnsNoChangesWithoutValidationCancellationOrIo() { using Harness h = new(); Assert.Equal(TerrainCatalogSaveStatus.NoChanges, h.Manager.Save().Status); Assert.Equal(0, h.Operations.Creates); }
    [Fact] public void Save_InvalidDraftChangesNeitherFileNorRuntimeAndDoesNotCancel() { using Harness h = new(); h.Manager.ViewModel.RemoveVariant(new(0), 15, 0); AssetContext before = h.Controller.Current; Assert.Equal(TerrainCatalogSaveStatus.InvalidDraft, h.Manager.Save().Status); Assert.Same(before, h.Controller.Current); }
    [Fact] public void Save_SamePathContextReplacementReturnsSourceContextChangedAndStaysDirty() { using Harness h = new(); h.Dirty(); Assert.True(h.Controller.TryOpen(h.Fixture.AssetDirectory)); Assert.Equal(TerrainCatalogSaveStatus.SourceContextChanged, h.Manager.Save().Status); Assert.True(h.Manager.ViewModel.IsDirty); }

    [Fact]
    public void Save_ThreeCallbacksWithMiddleFailureAttemptsAllBeforeWriteAndPreservesExternalState()
    {
        using Harness h = new();
        var calls = new List<int>();
        using var a = h.Controller.RegisterTerrainGestureCancellation(() => calls.Add(1));
        using var b = h.Controller.RegisterTerrainGestureCancellation(() => { calls.Add(2); throw new InvalidOperationException("middle"); });
        using var c = h.Controller.RegisterTerrainGestureCancellation(() => calls.Add(3));
        h.Dirty();
        string fileBefore = h.Operations.Text;
        AssetContext contextBefore = h.Controller.Current;
        string? selectionBefore = h.Workspace.ActiveDocument.SelectedTerrainId;
        byte[] mapBefore = MapCodec.Encode(h.Workspace.ActiveDocument.Session.Document);
        long historyBefore = h.Workspace.ActiveDocument.Session.HistoryVersion;

        Assert.Equal(TerrainCatalogSaveStatus.GestureCancellationFailed, h.Manager.Save().Status);

        Assert.Equal(new[] { 1, 2, 3 }, calls);
        Assert.Equal(0, h.Operations.Creates);
        Assert.Equal(fileBefore, h.Operations.Text);
        Assert.Same(contextBefore, h.Controller.Current);
        Assert.Equal(selectionBefore, h.Workspace.ActiveDocument.SelectedTerrainId);
        Assert.Equal(mapBefore, MapCodec.Encode(h.Workspace.ActiveDocument.Session.Document));
        Assert.Equal(historyBefore, h.Workspace.ActiveDocument.Session.HistoryVersion);
        Assert.True(h.Manager.ViewModel.IsDirty);
    }

    [Fact]
    public void Save_CancellationCallbackNestedTryOpenSaveAndControllerDisposeAreRejectedBeforeNestedMutation()
    {
        using Harness h = new();
        TerrainCatalogSaveResult? nestedSave = null;
        bool? nestedOpen = null;
        Exception? openFailure = null;
        Exception? disposeFailure = null;
        AssetContext source = h.Controller.Current;
        using var registration = h.Controller.RegisterTerrainGestureCancellation(() =>
        {
            nestedOpen = h.Controller.TryOpen(h.Fixture.AssetDirectory, out openFailure);
            nestedSave = h.Manager.Save();
            disposeFailure = Record.Exception(h.Controller.Dispose);
        });
        h.Dirty();

        Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status);

        Assert.False(nestedOpen);
        Assert.Equal("An asset replacement is already in progress.", openFailure!.Message);
        Assert.Equal(TerrainCatalogSaveStatus.ReplacementInProgress, nestedSave!.Status);
        Assert.Equal("An asset replacement is already in progress.", disposeFailure!.Message);
        Assert.NotSame(source, h.Controller.Current);
    }

    [Fact] public void Save_SuccessCancelsBeforeFirstFileOperationThenPublishesCanonicalBytes() { using Harness h = new(); var cancelled = false; using var registration = h.Controller.RegisterTerrainGestureCancellation(() => cancelled = true); h.Operations.BeforeCreate = () => Assert.True(cancelled); h.Dirty(); Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status); Assert.Equal(TerrainCatalogJson.Serialize(h.Controller.Current.Terrain.Source!), h.Operations.Text); }

    [Fact]
    public void Save_FileOperationFakeNestedTryOpenSaveAndDisposeAreRejectedAndOuterWriteWins()
    {
        using Harness h = new();
        TerrainCatalogSaveResult? nestedSave = null;
        bool? nestedOpen = null;
        Exception? openFailure = null;
        Exception? disposeFailure = null;
        h.Operations.BeforeCreate = () =>
        {
            nestedOpen = h.Controller.TryOpen(h.Fixture.AssetDirectory, out openFailure);
            nestedSave = h.Manager.Save();
            disposeFailure = Record.Exception(h.Controller.Dispose);
        };
        h.Dirty();

        Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status);

        Assert.False(nestedOpen);
        Assert.Equal("An asset replacement is already in progress.", openFailure!.Message);
        Assert.Equal(TerrainCatalogSaveStatus.ReplacementInProgress, nestedSave!.Status);
        Assert.Equal("An asset replacement is already in progress.", disposeFailure!.Message);
        Assert.Equal(TerrainCatalogJson.Serialize(h.Controller.Current.Terrain.Source!), h.Operations.Text);
    }

    [Fact] public void Save_SuccessPublishesIntoExistingContextWithoutReplacingCacheRendererOrTint() { using Harness h = new(); var cache = h.Controller.Current.Cache; var renderer = h.Controller.Current.Renderer; var tint = h.Controller.Current.TintCache; h.Dirty(); Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status); Assert.Same(cache, h.Controller.Current.Cache); Assert.Same(renderer, h.Controller.Current.Renderer); Assert.Same(tint, h.Controller.Current.TintCache); }

    [Theory]
    [InlineData("Create")]
    [InlineData("Write")]
    [InlineData("Flush")]
    [InlineData("Dispose")]
    [InlineData("Replace")]
    [InlineData("Move")]
    public void Save_CreateWriteFlushDisposeReplaceMoveFailurePreservesFileRuntimeSelectionMapHistoryDirtyDraftAndReleasesReservation(string phase)
    {
        using Harness h = new();
        h.Operations.DestinationExists = phase != "Move";
        h.Operations.FailurePhase = phase;
        h.Workspace.ActiveDocument.Brush = new MapTileLayer(1, 10);
        h.Workspace.ActiveDocument.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(h.Workspace.ActiveDocument.Session.CompleteStroke());
        h.Dirty();
        string fileBefore = h.Operations.Text;
        AssetContext contextBefore = h.Controller.Current;
        object? runtimeBefore = contextBefore.Terrain.Runtime;
        string? selectionBefore = h.Workspace.ActiveDocument.SelectedTerrainId;
        byte[] mapBefore = MapCodec.Encode(h.Workspace.ActiveDocument.Session.Document);
        long historyBefore = h.Workspace.ActiveDocument.Session.HistoryVersion;
        string draftBefore = TerrainCatalogJson.Serialize(h.Manager.ViewModel.Build());

        Assert.Equal(TerrainCatalogSaveStatus.WriteFailed, h.Manager.Save().Status);

        Assert.Equal(fileBefore, h.Operations.Text);
        Assert.Same(contextBefore, h.Controller.Current);
        Assert.Same(runtimeBefore, h.Controller.Current.Terrain.Runtime);
        Assert.Equal(selectionBefore, h.Workspace.ActiveDocument.SelectedTerrainId);
        Assert.Equal(mapBefore, MapCodec.Encode(h.Workspace.ActiveDocument.Session.Document));
        Assert.Equal(historyBefore, h.Workspace.ActiveDocument.Session.HistoryVersion);
        Assert.Equal(draftBefore, TerrainCatalogJson.Serialize(h.Manager.ViewModel.Build()));
        Assert.True(h.Manager.ViewModel.IsDirty);

        h.Operations.FailurePhase = null;
        Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status);
        Assert.False(h.Manager.ViewModel.IsDirty);
    }

    [Fact] public void Save_FailedWriteThenRetryOrAssetOpenSucceeds() { using Harness h = new(); h.Operations.FailurePhase = "Create"; h.Dirty(); Assert.Equal(TerrainCatalogSaveStatus.WriteFailed, h.Manager.Save().Status); h.Operations.FailurePhase = null; Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status); }

    [Fact]
    public void Save_RekeyedMiddleSelectionFollowsMappedIdAcrossAtLeastThreeEnabledSets()
    {
        using Harness h = new(("A",10),("B",11),("C",12),("D",13));
        string old = h.Manager.ViewModel.Sets[1].Id;
        h.Workspace.ActiveDocument.SelectedTerrainId = old;
        h.Manager.ViewModel.AddVariant(new(1), 0, new(1,14));
        string mapped = h.Manager.ViewModel.BuildPublishedIdRekeys()[old];

        Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status);

        Assert.Equal(mapped, h.Workspace.ActiveDocument.SelectedTerrainId);
        Assert.Equal(mapped, h.Controller.Current.Terrain.Runtime!.EnabledSets[1].Id);
    }

    [Fact] public void Save_RekeyTargetDisabledUsesOrdinaryFallback() { using Harness h = new(("A",10),("B",11)); h.Manager.ViewModel.TrySetStatus(new(1), TerrainReviewStatus.Disabled, out _); Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status); Assert.Equal(h.Controller.Current.Terrain.Runtime!.EnabledSets[0].Id, h.Workspace.ActiveDocument.SelectedTerrainId); }
    [Fact] public void Save_ValidZeroEnabledCatalogPublishesAndFallsBackTool() { using Harness h = new(); h.Manager.ViewModel.TrySetStatus(new(0), TerrainReviewStatus.Disabled, out _); Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status); Assert.False(h.Workspace.ActiveDocument.IsTerrainAvailable); }

    [Fact]
    public async System.Threading.Tasks.Task Save_FirstDocumentFirstObserverNestedTryOpenAndSaveAreRejectedWhileAllLaterObserversCommit()
    {
        using Harness h = new(("A", 10), ("B", 11));
        await h.Workspace.NewAsync();
        await h.Workspace.NewAsync();
        MapDocumentViewModel[] documents = h.Workspace.Documents.ToArray();
        TerrainCatalogSaveResult? nestedSave = null;
        bool? nestedOpen = null;
        Exception? openFailure = null;
        var notified = new bool[documents.Length];
        documents[0].CanvasInvalidated += () =>
        {
            notified[0] = true;
            nestedOpen = h.Controller.TryOpen(h.Fixture.AssetDirectory, out openFailure);
            nestedSave = h.Manager.Save();
        };
        for (var i = 1; i < documents.Length; i++)
        {
            int index = i;
            documents[i].CanvasInvalidated += () => notified[index] = true;
        }
        h.Dirty();

        Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status);

        Assert.False(nestedOpen);
        Assert.Equal("An asset replacement is already in progress.", openFailure!.Message);
        Assert.Equal(TerrainCatalogSaveStatus.ReplacementInProgress, nestedSave!.Status);
        Assert.All(notified, Assert.True);
        Assert.All(documents.Skip(1), document => Assert.Same(h.Controller.Current.Terrain, document.Terrain));
    }

    [Fact]
    public void Save_ObserverFailuresReturnSucceededWithOrderedWarningsAndDiskRuntimeEquality()
    {
        using Harness h = new();
        h.Dirty();
        h.Workspace.ActiveDocument.CanvasInvalidated += () => throw new InvalidOperationException("document-error");
        h.Manager.ViewModel.PropertyChanged += (_, _) => throw new InvalidOperationException("manager-error");

        TerrainCatalogSaveResult result = h.Manager.Save();

        Assert.Equal(TerrainCatalogSaveStatus.Succeeded, result.Status);
        Assert.Equal("document:0:CanvasInvalidated", result.Warnings[0].Scope);
        Assert.Equal("Document 0 canvas observer failed after asset publication: document-error", result.Warnings[0].Message);
        string[] properties = { "Sets", "SelectedSet", "Issues", "IsDirty", "CanSave" };
        Assert.Equal(properties.Select(property => $"terrain-manager:PropertyChanged:{property}"), result.Warnings.Skip(1).Select(warning => warning.Scope));
        Assert.Equal(properties.Select(property => $"Terrain manager property '{property}' observer failed after catalog publication: manager-error"), result.Warnings.Skip(1).Select(warning => warning.Message));
        Assert.Equal(TerrainCatalogJson.Serialize(h.Controller.Current.Terrain.Source!), h.Operations.Text);
        Assert.Equal(TerrainCatalogJson.Serialize(TerrainCatalogJson.Parse(h.Operations.Text)), TerrainCatalogJson.Serialize(h.Controller.Current.Terrain.Source!));
    }

    [Fact] public void Save_PublishedIdRekeysResetOnlyAfterSuccessfulPublication() { using Harness h = new(("A",10)); h.Manager.ViewModel.AddVariant(new(0), 0, new(1,14)); h.Operations.FailurePhase = "Create"; Assert.Equal(TerrainCatalogSaveStatus.WriteFailed, h.Manager.Save().Status); Assert.NotEmpty(h.Manager.ViewModel.BuildPublishedIdRekeys()); h.Operations.FailurePhase = null; Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status); Assert.Contains(h.Manager.ViewModel.BuildPublishedIdRekeys(), pair => pair.Key == pair.Value); }
    [Fact] public void Save_SuccessMarksCleanAndSecondSaveReturnsNoChanges() { using Harness h = new(); h.Dirty(); Assert.Equal(TerrainCatalogSaveStatus.Succeeded, h.Manager.Save().Status); Assert.False(h.Manager.ViewModel.IsDirty); Assert.Equal(TerrainCatalogSaveStatus.NoChanges, h.Manager.Save().Status); }

    private sealed class Harness : IDisposable
    {
        internal readonly AssetFixture Fixture = new();
        internal readonly FakeEditorDialogs Dialogs = new();
        internal readonly WorkspaceViewModel Workspace;
        internal readonly AssetContextController Controller;
        internal readonly RecordingOperations Operations = new();
        internal readonly TerrainCatalogManager Manager;
        internal Harness(params (string Name, int Graphic)[] definitions)
        {
            if (definitions.Length == 0) definitions = new[] { ("A", 10) };
            string frames = string.Join(',', Enumerable.Range(10, 10).Select(i => $"\"{i}\":[0,0,32,32]"));
            Fixture.WriteManifest($"{{\"tileSize\":32,\"sheets\":{{\"1\":{{{frames}}}}}}}");
            Fixture.WriteTerrainCatalog(TerrainTestData.Catalog(definitions));
            Workspace = new WorkspaceViewModel(Dialogs, new MapFileStore());
            Controller = new AssetContextController(Workspace, new AppSettingsStore(Path.Combine(Fixture.Root, "settings.json")));
            Assert.True(Controller.TryOpen(Fixture.AssetDirectory));
            Operations.DestinationExists = true;
            Operations.Text = File.ReadAllText(Path.Combine(Fixture.AssetDirectory, "terrain-brushes.json"));
            Manager = new TerrainCatalogManager(Controller, Controller.Current, new TerrainCatalogFileStore(Operations));
        }
        internal void Dirty() => Manager.ViewModel.Rename(new(0), "Changed");
        public void Dispose() { Controller.Dispose(); Fixture.Dispose(); }
    }

    private sealed class RecordingOperations : ITerrainCatalogFileOperations
    {
        internal int Creates; internal bool DestinationExists; internal string? FailurePhase; internal Action? BeforeCreate; internal string Text = ""; private MemoryStream? stream;
        public bool Exists(string path) => DestinationExists;
        public Stream CreateFile(string path) { Creates++; BeforeCreate?.Invoke(); if (FailurePhase == "Create") throw new IOException("Create"); stream = new FailureStream(this); return stream; }
        public void FlushToDisk(Stream value) { if (FailurePhase == "Flush") throw new IOException("Flush"); }
        public void Replace(string sourcePath, string destinationPath) { if (FailurePhase == "Replace") throw new IOException("Replace"); Text = System.Text.Encoding.UTF8.GetString(stream!.ToArray()); }
        public void Move(string sourcePath, string destinationPath) { if (FailurePhase == "Move") throw new IOException("Move"); Text = System.Text.Encoding.UTF8.GetString(stream!.ToArray()); }
        public void Delete(string path) { }
        private sealed class FailureStream : MemoryStream { private readonly RecordingOperations owner; internal FailureStream(RecordingOperations owner) => this.owner = owner; public override void Write(byte[] buffer, int offset, int count) { if (owner.FailurePhase == "Write") throw new IOException("Write"); base.Write(buffer, offset, count); } protected override void Dispose(bool disposing) { if (owner.FailurePhase == "Dispose") throw new IOException("Dispose"); base.Dispose(disposing); } }
    }
}
