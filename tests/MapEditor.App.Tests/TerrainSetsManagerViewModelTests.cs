using System;
using System.IO;
using System.Linq;
using System.Threading;
using MapEditor.App.Terrain;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using MapEditor.Rendering;
using MapEditor.Rendering.Terrain;
using Xunit;

namespace MapEditor.App.Tests;

public sealed class TerrainSetsManagerViewModelTests
{
    private static SpriteManifest Manifest(string frames) => SpriteManifest.Parse($"{{\"tileSize\":32,\"sheets\":{{\"1\":{{{frames}}}}}}}");

    [Fact]
    public void SelectedSetKey_WrongThreadThrowsBeforeMutationOrNotification()
    {
        TerrainCatalogDraft draft = new(TerrainTestData.Catalog(("A", 10)), Manifest("\"10\":[0,0,32,32]"));
        var manager = new TerrainSetsManagerViewModel(draft);
        var notifications = 0;
        manager.PropertyChanged += (_, _) => notifications++;
        Exception? failure = null;
        var thread = new Thread(() => failure = Record.Exception(() => manager.SelectedSetKey = new(0)));

        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Null(manager.SelectedSetKey);
        Assert.Null(manager.SelectedSet);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void MaskRows_ExposeVisualNeighborhoodAndResolvedFrameState()
    {
        TerrainCatalogDraft draft = new(TerrainTestData.Catalog(("A", 10)), Manifest("\"10\":[0,0,32,32]"));
        TerrainMaskDraft row = draft.Sets[0].Masks.Single(x => x.Mask == 3);

        Assert.True(row.North);
        Assert.True(row.East);
        Assert.False(row.South);
        Assert.False(row.West);
        Assert.Equal(TerrainFrameState.Resolved, row.Variants[0].FrameState);
    }

    [Fact]
    public void TrySetEnabled_CompleteUniqueResolvedSetSucceeds()
    {
        TerrainCatalog source = SetStatus(TerrainTestData.Catalog(("A", 10)), 0, TerrainReviewStatus.Pending);
        TerrainCatalogDraft draft = new(source, Manifest("\"10\":[0,0,32,32]"));

        Assert.True(draft.TrySetStatus(new(0), TerrainReviewStatus.Enabled, out var issues));
        Assert.Empty(issues);
        Assert.Equal(TerrainReviewStatus.Enabled, draft.Build().Sets[0].Status);
    }

    [Fact]
    public void TrySetEnabled_MissingMaskFrameOrConflictReturnsExactIssuesAndKeepsStatus()
    {
        TerrainCatalog source = SetStatus(TerrainTestData.Catalog(("A", 10)), 0, TerrainReviewStatus.Pending);
        TerrainSetDefinition set = source.Sets[0];
        source = Replace(source, 0, new(set.Id, set.DisplayName, set.Status, set.Topology, set.Metrics, set.Masks.Where(x => x.Mask != 15), set.Members, set.Diagnostics));
        TerrainCatalogDraft draft = new(source, Manifest(""));

        Assert.False(draft.TrySetStatus(new(0), TerrainReviewStatus.Enabled, out var issues));
        Assert.Equal(new[] { "enabled-mask-missing", "terrain-frame-missing" }, issues.Select(x => x.Code).OrderBy(x => x));
        Assert.Equal(TerrainReviewStatus.Pending, draft.Build().Sets[0].Status);

        TerrainCatalog conflictSource = TerrainTestData.Catalog(("Enabled", 10), ("Pending", 11));
        TerrainSetDefinition pending = conflictSource.Sets[1];
        var shared = new TerrainGraphicReference(1, 10);
        var own = new TerrainGraphicReference(1, 11);
        pending = new TerrainSetDefinition(
            TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { shared, own }),
            pending.DisplayName,
            TerrainReviewStatus.Pending,
            pending.Topology,
            pending.Metrics,
            pending.Masks.Select(mask => new TerrainMaskDefinition(mask.Mask, mask.Mask == 0 ? new[] { shared, own } : new[] { shared })),
            new[]
            {
                new TerrainMemberDefinition(shared, TerrainMemberProvenance.MapObserved),
                new TerrainMemberDefinition(own, TerrainMemberProvenance.ImageOnly)
            },
            pending.Diagnostics);
        conflictSource = Replace(conflictSource, 1, pending);
        SpriteManifest conflictManifest = Manifest("\"10\":[0,0,32,32],\"11\":[0,0,32,32]");
        TerrainCatalogDraft conflictDraft = new(conflictSource, conflictManifest);
        string[] conflictIds = new[] { conflictSource.Sets[0].Id, pending.Id }.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string owners = string.Join(", ", conflictIds.Select(id => $"'{id}'"));
        TerrainValidationIssue[] expectedIssues = conflictIds.Select(id => new TerrainValidationIssue(
            "enabled-member-conflict",
            $"Enabled terrain '{id}' member (1,10) is shared by [{owners}].",
            id,
            reference: shared)).ToArray();

        Assert.False(conflictDraft.TrySetStatus(new(1), TerrainReviewStatus.Enabled, out var conflictIssues));
        Assert.Equal(expectedIssues, conflictIssues);
        Assert.Equal(TerrainReviewStatus.Pending, conflictDraft.Build().Sets[1].Status);
    }

    [Fact]
    public void SetPendingOrDisabled_AllowsIncompleteConflictingReviewData()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10), ("B", 10));
        TerrainCatalogDraft draft = new(source, Manifest(""));

        Assert.True(draft.TrySetStatus(new(0), TerrainReviewStatus.Pending, out var pendingIssues));
        Assert.True(draft.TrySetStatus(new(1), TerrainReviewStatus.Disabled, out var disabledIssues));
        Assert.Empty(pendingIssues);
        Assert.Empty(disabledIssues);
    }

    [Fact]
    public void RemoveOneOrAllOrphansThenSerializeParseValidateRoundTripsExactly()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10));
        TerrainSetDefinition set = source.Sets[0];
        source = Replace(source, 0, new(set.Id, set.DisplayName, TerrainReviewStatus.Pending, TerrainTopology.EightWay, set.Metrics,
            TerrainMasks.Required(TerrainTopology.EightWay).Select(mask => new TerrainMaskDefinition(mask, new[] { new TerrainGraphicReference(1, 10) })), set.Members, set.Diagnostics));
        source = Replace(source, 0, Copy(source.Sets[0], TerrainGeneratedId.Create(TerrainTopology.EightWay, source.Sets[0].Members.Select(x => x.Reference))));
        TerrainCatalogDraft draft = new(source, Manifest("\"10\":[0,0,32,32]"));
        Assert.True(draft.ChangeTopology(new(0), TerrainTopology.FourWay).Succeeded);
        Assert.NotEmpty(draft.Sets[0].OrphanMasks);

        int first = draft.Sets[0].OrphanMasks[0].Mask;
        Assert.True(draft.RemoveOrphanMask(new(0), first).Succeeded);
        Assert.True(draft.RemoveAllOrphanMasks(new(0)).Succeeded);
        TerrainCatalog built = draft.Build();
        TerrainCatalog parsed = TerrainCatalogJson.Parse(TerrainCatalogJson.Serialize(built));
        Assert.Equal(TerrainCatalogJson.Serialize(built), TerrainCatalogJson.Serialize(parsed));
        Assert.Empty(TerrainCatalogValidator.Validate(parsed));
    }

    [Fact]
    public void EditThenRevertIsCleanAndRejectedEditNeverDirties()
    {
        TerrainCatalogDraft draft = new(TerrainTestData.Catalog(("A", 10)), Manifest("\"10\":[0,0,32,32]"));
        draft.Rename(new(0), "B");
        Assert.True(draft.IsDirty);
        draft.Rename(new(0), "A");
        Assert.False(draft.IsDirty);
        Assert.False(draft.RegenerateId(new TerrainDraftKey(99)).Succeeded);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void PublishedIdRekeysTrackOnlyUniqueOriginallyEnabledIds()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10), ("B", 11), ("C", 12), ("D", 13));
        TerrainSetDefinition[] sets = source.Sets.ToArray();
        sets[1] = Copy(sets[1], sets[0].Id);
        sets[2] = new(sets[2].Id, sets[2].DisplayName, TerrainReviewStatus.Pending, sets[2].Topology, sets[2].Metrics, sets[2].Masks, sets[2].Members, sets[2].Diagnostics);
        source = new(source.SchemaVersion, source.GeneratorVersion, source.CorpusFingerprint, source.Settings, sets, source.Diagnostics);
        TerrainCatalogDraft draft = new(source, Manifest("\"10\":[0,0,32,32],\"11\":[0,0,32,32],\"12\":[0,0,32,32],\"13\":[0,0,32,32],\"14\":[0,0,32,32]"));
        string duplicate = sets[0].Id;
        string pending = sets[2].Id;
        string oldUnique = sets[3].Id;
        Assert.True(draft.AddVariant(new(3), 0, new(1, 14)).Succeeded);

        var rekeys = draft.BuildPublishedIdRekeys();
        Assert.DoesNotContain(duplicate, rekeys.Keys);
        Assert.DoesNotContain(pending, rekeys.Keys);
        Assert.Equal(oldUnique, Assert.Single(rekeys).Key);
        Assert.Equal(TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { new TerrainGraphicReference(1, 13), new TerrainGraphicReference(1, 14) }), Assert.Single(rekeys).Value);
    }

    [Fact]
    public void DraftMutations_DoNotChangePublishedResolverFileOrMapHistory()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10));
        SpriteManifest manifest = Manifest("\"10\":[0,0,32,32],\"11\":[0,0,32,32]");
        TerrainAssetLoadResult published = TerrainAssetCatalog.Validate(source, manifest);
        TerrainAssetCatalog runtime = published.Runtime!;
        TerrainMapResolver resolver = runtime.Resolver;
        var map = new MapEditSession(MapDocument.Create(4, 3));
        byte[] mapBytes = MapCodec.Encode(map.Document);
        long historyVersion = map.HistoryVersion;
        string catalogJson = TerrainCatalogJson.Serialize(source);
        string path = Path.GetTempFileName();
        File.WriteAllText(path, catalogJson);
        byte[] fileBytes = File.ReadAllBytes(path);
        try
        {
            TerrainCatalogDraft draft = new(published.Source!, manifest);
            TerrainSetsManagerViewModel manager = new(draft) { SelectedSetKey = new(0) };
            manager.Rename(new(0), "Changed");
            Assert.True(manager.AddVariant(new(0), 0, new(1, 11)).Succeeded);
            Assert.True(manager.TrySetStatus(new(0), TerrainReviewStatus.Pending, out _));
            Assert.True(manager.RemoveVariant(new(0), 15, 0).Succeeded);
            string draftBeforeRejectedEnable = TerrainCatalogJson.Serialize(manager.Build());

            Assert.False(manager.TrySetStatus(new(0), TerrainReviewStatus.Enabled, out var enableIssues));
            Assert.Equal("enabled-mask-empty", Assert.Single(enableIssues).Code);
            Assert.Equal(draftBeforeRejectedEnable, TerrainCatalogJson.Serialize(manager.Build()));
            Assert.Equal(TerrainReviewStatus.Pending, manager.Build().Sets[0].Status);

            Assert.Equal(catalogJson, TerrainCatalogJson.Serialize(published.Source!));
            Assert.Equal(fileBytes, File.ReadAllBytes(path));
            Assert.Same(runtime, published.Runtime);
            Assert.Same(resolver, published.Runtime!.Resolver);
            Assert.Equal(new TerrainGraphicReference(1, 10), published.Runtime.Representative);
            Assert.Equal(mapBytes, MapCodec.Encode(map.Document));
            Assert.Equal(historyVersion, map.HistoryVersion);
            Assert.False(map.CanUndo);
            Assert.Equal(new TerrainDraftKey(0), manager.SelectedSetKey);
        }
        finally { File.Delete(path); }
    }

    private static TerrainCatalog SetStatus(TerrainCatalog source, int index, TerrainReviewStatus status)
    {
        TerrainSetDefinition set = source.Sets[index];
        return Replace(source, index, new(set.Id, set.DisplayName, status, set.Topology, set.Metrics, set.Masks, set.Members, set.Diagnostics));
    }

    private static TerrainCatalog Replace(TerrainCatalog source, int index, TerrainSetDefinition set)
    {
        TerrainSetDefinition[] sets = source.Sets.ToArray();
        sets[index] = set;
        return new(source.SchemaVersion, source.GeneratorVersion, source.CorpusFingerprint, source.Settings, sets, source.Diagnostics);
    }

    private static TerrainSetDefinition Copy(TerrainSetDefinition set, string id) => new(id, set.DisplayName, set.Status, set.Topology, set.Metrics, set.Masks, set.Members, set.Diagnostics);
}
