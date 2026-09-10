using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.App.Terrain;
using MapEditor.App.Tests.Fixtures;
using MapEditor.Core.Terrain;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public sealed class TerrainCatalogDraftTests
{
    private static SpriteManifest Manifest(params int[] graphics) => SpriteManifest.Parse(
        $"{{\"tileSize\":32,\"sheets\":{{\"1\":{{{string.Join(',', graphics.Select(x => $"\"{x}\":[0,0,32,32]"))}}}}}}}");

    private static TerrainCatalogDraft Draft(TerrainCatalog catalog, params int[] graphics) => new(catalog, Manifest(graphics));

    [Fact]
    public void CloneBuild_RoundTripsGeneratedMetadataProvenanceAndDiagnostics()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("Grass", 10));
        TerrainCatalogDraft draft = Draft(source, 10);

        Assert.Equal(TerrainCatalogJson.Serialize(source), TerrainCatalogJson.Serialize(draft.Build()));
        Assert.NotSame(source, draft.Build());
        Assert.NotSame(source.Sets[0], draft.Build().Sets[0]);
    }

    [Fact]
    public void Clone_BlankDuplicateAndMismatchedIdsRemainDistinctBySourceOrderKey()
    {
        TerrainCatalog source = WithSets(TerrainTestData.Catalog(("Blank", 10), ("Duplicate A", 11), ("Duplicate B", 12), ("Mismatch", 13)),
            (0, ""), (1, "duplicate"), (2, "duplicate"), (3, "mismatched-authored-id"));
        TerrainCatalogDraft draft = Draft(source, 10, 11, 12, 13);
        var manager = new MapEditor.App.ViewModels.TerrainSetsManagerViewModel(draft);

        Assert.Equal(new[] { 0, 1, 2, 3 }, draft.Sets.Select(x => x.Key.Value));
        Assert.Equal(new[] { "", "duplicate", "duplicate", "mismatched-authored-id" }, draft.Build().Sets.Select(x => x.Id));
        Assert.NotEqual(source.Sets[3].Id, TerrainGeneratedId.Create(source.Sets[3].Topology, source.Sets[3].Members.Select(member => member.Reference)));

        for (var i = 0; i < source.Sets.Count; i++)
        {
            TerrainDraftKey key = new(i);
            manager.SelectedSetKey = key;
            manager.Rename(key, $"Selected {i}");
            Assert.Equal(key, manager.SelectedSetKey);
            Assert.Equal($"Selected {i}", manager.SelectedSet!.DisplayName);
        }

        Assert.Equal(new[] { "", "duplicate", "duplicate", "mismatched-authored-id" }, draft.Build().Sets.Select(x => x.Id));
    }

    [Fact]
    public void RegenerateId_RepairsBlankMismatchAndDifferingDuplicateIds()
    {
        TerrainCatalog source = WithSets(TerrainTestData.Catalog(("Blank", 10), ("Mismatch", 11), ("Duplicate A", 12), ("Duplicate B", 13)),
            (0, ""), (1, "mismatch"), (2, "duplicate"), (3, "duplicate"));
        TerrainCatalogDraft draft = Draft(source, 10, 11, 12, 13);
        string[] generatedIds = source.Sets
            .Select(set => TerrainGeneratedId.Create(set.Topology, set.Members.Select(member => member.Reference)))
            .ToArray();
        Assert.NotEqual(generatedIds[2], generatedIds[3]);

        foreach (TerrainSetDraft set in draft.Sets)
            Assert.True(draft.RegenerateId(set.Key).Succeeded);

        Assert.Equal(generatedIds, draft.Build().Sets.Select(set => set.Id));
    }

    [Fact]
    public void RegenerateId_TrueIdentityCollisionReturnsExactPartOneIssueWithoutMutation()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10), ("B", 10));
        source = WithSets(source, (0, "bad-a"), (1, "bad-b"));
        TerrainCatalogDraft draft = Draft(source, 10);
        string before = TerrainCatalogJson.Serialize(draft.Build());

        TerrainDraftMutationResult result = draft.RegenerateId(new TerrainDraftKey(0));

        Assert.False(result.Succeeded);
        TerrainValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal("terrain-id-duplicate", issue.Code);
        Assert.Equal(before, TerrainCatalogJson.Serialize(draft.Build()));
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void RenameStatusAndVariantReorder_DoNotChangeId()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10));
        TerrainSetDefinition set = source.Sets[0];
        source = Replace(source, 0, Copy(set, masks: set.Masks.Select(x => x.Mask == 0 ? new TerrainMaskDefinition(0, new[] { new TerrainGraphicReference(1, 10), new TerrainGraphicReference(1, 11) }) : x), members: new[] { set.Members[0], new TerrainMemberDefinition(new(1, 11), TerrainMemberProvenance.ImageOnly) }, id: TerrainGeneratedId.Create(set.Topology, new[] { new TerrainGraphicReference(1, 10), new TerrainGraphicReference(1, 11) })));
        TerrainCatalogDraft draft = Draft(source, 10, 11);
        string id = source.Sets[0].Id;

        draft.Rename(new(0), "Renamed");
        Assert.True(draft.TrySetStatus(new(0), TerrainReviewStatus.Pending, out _));
        Assert.True(draft.ReorderVariant(new(0), 0, 0, 1).Succeeded);

        Assert.Equal(id, draft.Build().Sets[0].Id);
        Assert.Equal(new[] { 11, 10 }, draft.Build().Sets[0].Masks.Single(x => x.Mask == 0).Variants.Select(x => x.Graphic));
    }

    [Fact]
    public void ChangeTopology_RebuildsRowsRecomputesAuthoredIdAndRetainsSelectionKey()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10));
        TerrainSetDefinition set = source.Sets[0];
        var reference = new TerrainGraphicReference(1, 10);
        string eightWayId = TerrainGeneratedId.Create(TerrainTopology.EightWay, new[] { reference });
        source = Replace(source, 0, new TerrainSetDefinition(
            eightWayId,
            set.DisplayName,
            TerrainReviewStatus.Pending,
            TerrainTopology.EightWay,
            set.Metrics,
            TerrainMasks.Required(TerrainTopology.EightWay)
                .Where(mask => mask != 5)
                .Select(mask => new TerrainMaskDefinition(mask, new[] { reference })),
            set.Members,
            set.Diagnostics));
        TerrainCatalogDraft draft = Draft(source, 10);
        var manager = new MapEditor.App.ViewModels.TerrainSetsManagerViewModel(draft) { SelectedSetKey = new(0) };

        TerrainDraftMutationResult result = manager.ChangeTopology(new(0), TerrainTopology.FourWay);

        Assert.True(result.Succeeded);
        Assert.Equal(TerrainMasks.Required(TerrainTopology.FourWay), draft.Sets[0].Masks.Select(x => x.Mask));
        Assert.Empty(draft.Sets[0].Masks.Single(x => x.Mask == 5).Variants);
        Assert.All(draft.Sets[0].Masks.Where(x => x.Mask != 5), row => Assert.Equal(reference, Assert.Single(row.Variants).Reference));
        Assert.Equal(TerrainMasks.Required(TerrainTopology.EightWay).Where(mask => mask > 15), draft.Sets[0].OrphanMasks.Select(x => x.Mask));
        Assert.All(draft.Sets[0].OrphanMasks, row => Assert.Equal(reference, Assert.Single(row.Variants).Reference));
        Assert.Equal(new TerrainDraftKey(0), manager.SelectedSetKey);
        Assert.NotEqual(eightWayId, draft.Build().Sets[0].Id);
        Assert.Equal(TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { reference }), draft.Build().Sets[0].Id);
    }

    [Fact]
    public void ChangeTopology_WithNoMembersReturnsFailureWithoutMutation()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10));
        source = Replace(source, 0, Copy(source.Sets[0], members: Array.Empty<TerrainMemberDefinition>()));
        TerrainCatalogDraft draft = Draft(source, 10);
        string before = TerrainCatalogJson.Serialize(draft.Build());

        TerrainDraftMutationResult result = draft.ChangeTopology(new(0), TerrainTopology.EightWay);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == "member-required");
        Assert.Equal(before, TerrainCatalogJson.Serialize(draft.Build()));
        Assert.Equal(TerrainTopology.FourWay, draft.Build().Sets[0].Topology);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void AddVariant_WithDuplicateMembersReturnsFailureWithoutMutation()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10));
        TerrainSetDefinition set = source.Sets[0];
        source = Replace(source, 0, Copy(set, members: new[] { set.Members[0], set.Members[0] }));
        TerrainCatalogDraft draft = Draft(source, 10, 11);
        string before = TerrainCatalogJson.Serialize(draft.Build());

        TerrainDraftMutationResult result = draft.AddVariant(new(0), 0, new(1, 11));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == "member-duplicate");
        Assert.Equal(before, TerrainCatalogJson.Serialize(draft.Build()));
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void RemoveVariant_LeavingDuplicateMembersReturnsFailureWithoutMutation()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10));
        TerrainSetDefinition set = source.Sets[0];
        var added = new TerrainGraphicReference(1, 11);
        source = Replace(source, 0, Copy(set,
            id: TerrainGeneratedId.Create(set.Topology, new[] { set.Members[0].Reference, added }),
            masks: set.Masks.Select(row => row.Mask == 0 ? new TerrainMaskDefinition(row.Mask, new[] { set.Members[0].Reference, added }) : row),
            members: new[] { set.Members[0], set.Members[0], new TerrainMemberDefinition(added, TerrainMemberProvenance.ImageOnly) }));
        TerrainCatalogDraft draft = Draft(source, 10, 11);
        string before = TerrainCatalogJson.Serialize(draft.Build());

        TerrainDraftMutationResult result = draft.RemoveVariant(new(0), 0, 1);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == "member-duplicate");
        Assert.Equal(before, TerrainCatalogJson.Serialize(draft.Build()));
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void RemoveLastVariantAndMember_SucceedsWithOldIdAndMemberRequiredIssue()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10));
        TerrainSetDefinition set = source.Sets[0];
        TerrainGraphicReference reference = set.Members[0].Reference;
        source = Replace(source, 0, Copy(set,
            masks: set.Masks.Select(row => new TerrainMaskDefinition(row.Mask, row.Mask == 0 ? new[] { reference } : Array.Empty<TerrainGraphicReference>())),
            members: new[] { new TerrainMemberDefinition(reference, TerrainMemberProvenance.ImageOnly) }));
        TerrainCatalogDraft draft = Draft(source, 10);

        TerrainDraftMutationResult result = draft.RemoveVariant(new(0), 0, 0);

        Assert.True(result.Succeeded);
        TerrainSetDefinition updated = draft.Build().Sets[0];
        Assert.Equal(set.Id, updated.Id);
        Assert.Empty(updated.Members);
        Assert.Empty(updated.Masks.Single(row => row.Mask == 0).Variants);
        Assert.Equal(new TerrainValidationIssue("member-required", $"Terrain '{set.Id}' must contain at least one member.", set.Id),
            Assert.Single(draft.Issues, issue => issue.Code == "member-required"));
        Assert.True(draft.IsDirty);
    }

    [Fact]
    public void AddNewMemberAndRemoveFinalImageOnlyUse_RecomputeIdWhileUnusedMapObservedMemberDoesNot()
    {
        TerrainCatalogDraft draft = Draft(TerrainTestData.Catalog(("A", 10)), 10, 11);
        string original = draft.Build().Sets[0].Id;

        Assert.True(draft.AddVariant(new(0), 0, new(1, 11)).Succeeded);
        Assert.NotEqual(original, draft.Build().Sets[0].Id);
        Assert.Equal(TerrainMemberProvenance.ImageOnly, draft.Build().Sets[0].Members.Single(x => x.Reference.Graphic == 11).Provenance);
        Assert.True(draft.RemoveVariant(new(0), 0, 1).Succeeded);
        Assert.Equal(original, draft.Build().Sets[0].Id);
        Assert.Single(draft.Build().Sets[0].Members);
        Assert.True(draft.RemoveVariant(new(0), 0, 0).Succeeded);
        Assert.Single(draft.Build().Sets[0].Members);
        Assert.Equal(original, draft.Build().Sets[0].Id);
    }

    [Fact]
    public void AddRemoveReorderVariant_PreservesExactUserOrderAndSynchronizesMemberProvenance()
    {
        TerrainCatalogDraft draft = Draft(TerrainTestData.Catalog(("A", 10)), 10, 11, 12);
        draft.AddVariant(new(0), 0, new(1, 11));
        draft.AddVariant(new(0), 0, new(1, 12));
        draft.ReorderVariant(new(0), 0, 2, 0);
        draft.RemoveVariant(new(0), 0, 1);

        TerrainSetDefinition set = draft.Build().Sets[0];
        Assert.Equal(new[] { 12, 11 }, set.Masks.Single(x => x.Mask == 0).Variants.Select(x => x.Graphic));
        Assert.Equal(new[] { 10, 11, 12 }, set.Members.Select(x => x.Reference.Graphic));
        Assert.Equal(TerrainMemberProvenance.MapObserved, set.Members[0].Provenance);
        Assert.All(set.Members.Skip(1), x => Assert.Equal(TerrainMemberProvenance.ImageOnly, x.Provenance));
    }

    [Fact]
    public void IdentityChangingEdit_CollidingWithExistingSetRejectsWithoutAnyDraftOrSelectionMutation()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("A", 10), ("B", 11));
        TerrainSetDefinition second = source.Sets[1];
        source = Replace(source, 1, Copy(second, masks: second.Masks.Select(x => new TerrainMaskDefinition(x.Mask, new[] { new TerrainGraphicReference(1, 10), new TerrainGraphicReference(1, 11) })), members: new[] { new TerrainMemberDefinition(new(1, 10), TerrainMemberProvenance.ImageOnly), second.Members[0] }, id: TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { new TerrainGraphicReference(1, 10), new TerrainGraphicReference(1, 11) })));
        TerrainCatalogDraft draft = Draft(source, 10, 11);
        var manager = new MapEditor.App.ViewModels.TerrainSetsManagerViewModel(draft) { SelectedSetKey = new(0) };
        string before = TerrainCatalogJson.Serialize(draft.Build());
        TerrainSetDraft selected = manager.SelectedSet!;
        IReadOnlyList<TerrainValidationIssue> issues = draft.Issues;

        TerrainDraftMutationResult result = manager.AddVariant(new(0), 0, new(1, 11));

        Assert.False(result.Succeeded);
        Assert.Equal("terrain-id-duplicate", Assert.Single(result.Issues).Code);
        Assert.Equal(before, TerrainCatalogJson.Serialize(draft.Build()));
        Assert.Same(selected, manager.SelectedSet);
        Assert.Same(issues, draft.Issues);
        Assert.Equal(new TerrainDraftKey(0), manager.SelectedSetKey);
        Assert.False(draft.IsDirty);
    }

    private static TerrainCatalog WithSets(TerrainCatalog source, params (int Index, string Id)[] ids)
    {
        TerrainSetDefinition[] sets = source.Sets.ToArray();
        foreach ((int index, string id) in ids)
            sets[index] = Copy(sets[index], id: id);
        return new(source.SchemaVersion, source.GeneratorVersion, source.CorpusFingerprint, source.Settings, sets, source.Diagnostics);
    }

    private static TerrainCatalog Replace(TerrainCatalog source, int index, TerrainSetDefinition replacement)
    {
        TerrainSetDefinition[] sets = source.Sets.ToArray();
        sets[index] = replacement;
        return new(source.SchemaVersion, source.GeneratorVersion, source.CorpusFingerprint, source.Settings, sets, source.Diagnostics);
    }

    private static TerrainSetDefinition Copy(TerrainSetDefinition set, string? id = null, System.Collections.Generic.IEnumerable<TerrainMaskDefinition>? masks = null, System.Collections.Generic.IEnumerable<TerrainMemberDefinition>? members = null) =>
        new(id ?? set.Id, set.DisplayName, set.Status, set.Topology, set.Metrics, masks ?? set.Masks, members ?? set.Members, set.Diagnostics);
}
