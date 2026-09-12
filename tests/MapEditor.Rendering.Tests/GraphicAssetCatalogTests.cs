using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class GraphicAssetCatalogTests
{
    private const string SpriteJson = """
        { "tileSize": 32, "sheets": {
          "10": { "1": [0, 0, 4, 4], "2": [4, 0, 4, 4] },
          "20": { "1": [0, 0, 4, 4] },
          "30": { "1": [0, 0, 4, 4], "2": [4, 0, 4, 4], "3": [8, 0, 4, 4] }
        } }
        """;

    private const string AnimationJson = """
        { "version": 1,
          "sheets": {
            "30": { "categories": [ { "name": "Hair", "id": 2 }, { "name": "Body", "id": 1 } ] },
            "10": { "categories": [ { "name": "Body", "id": 1 }, { "name": "Tiles" } ] }
          },
          "animations": [
            { "ownerSheet": 30, "id": 2, "fps": 4, "frames": [[30, 2], [30, 3]] },
            { "ownerSheet": 30, "id": 1, "fps": 8, "frames": [[10, 1], [30, 1]] },
            { "ownerSheet": 10, "id": 1, "fps": 8, "frames": [[10, 1], [10, 2]] }
          ] }
        """;

    [Fact]
    public void Create_NullCategoryReturnsAllSpriteSheetsIncludingUncategorized()
    {
        GraphicAssetCatalog catalog = CreateCatalog();

        Assert.Equal(new[] { 10, 20, 30 }, catalog.GetSheets(null));
    }

    [Fact]
    public void Create_NamedCategoriesReturnSortedEligibleSheets()
    {
        GraphicAssetCatalog catalog = CreateCatalog();

        Assert.Equal(new[] { 10, 30 }, catalog.GetSheets(GraphicCategory.Body));
        Assert.Equal(new[] { 30 }, catalog.GetSheets(GraphicCategory.Hair));
        Assert.Equal(new[] { 10 }, catalog.GetSheets(GraphicCategory.Tiles));
        Assert.Empty(catalog.GetSheets(GraphicCategory.Eyes));
    }

    [Fact]
    public void Create_SheetReturnsAllCategoryMappingsAndUnknownSheetsReturnNone()
    {
        GraphicAssetCatalog catalog = CreateCatalog();

        Assert.Equal(new[]
        {
            new GraphicCategoryMapping(GraphicCategory.Body, 1),
            new GraphicCategoryMapping(GraphicCategory.Hair, 2)
        }, catalog.GetMappings(30));
        Assert.Equal(new[]
        {
            new GraphicCategoryMapping(GraphicCategory.Body, 1),
            new GraphicCategoryMapping(GraphicCategory.Tiles, null)
        }, catalog.GetMappings(10));
        Assert.Empty(catalog.GetMappings(20));
        Assert.Empty(catalog.GetMappings(999));
    }

    [Fact]
    public void Create_FrameReturnsContainingAnimationsInOwnerAndIdOrder()
    {
        GraphicAssetCatalog catalog = CreateCatalog();

        IReadOnlyList<GraphicAnimation> animations = catalog.GetAnimations(new SpriteReference(10, 1));
        Assert.Equal(2, animations.Count);
        Assert.Equal(new GraphicAnimationKey(10, 1), animations[0].Key);
        Assert.Equal(new GraphicAnimationKey(30, 1), animations[1].Key);
        Assert.Empty(catalog.GetAnimations(new SpriteReference(20, 1)));
    }

    [Fact]
    public void Create_TryGetAnimationReturnsOrderedFramesAndFps()
    {
        GraphicAssetCatalog catalog = CreateCatalog();

        Assert.True(catalog.TryGetAnimation(new GraphicAnimationKey(30, 1), out GraphicAnimation animation));
        Assert.Equal(8, animation.FramesPerSecond);
        Assert.Equal(new[] { new SpriteReference(10, 1), new SpriteReference(30, 1) }, animation.Frames);

        Assert.True(catalog.TryGetAnimation(new GraphicAnimationKey(30, 2), out GraphicAnimation walk));
        Assert.Equal(4, walk.FramesPerSecond);

        Assert.False(catalog.TryGetAnimation(new GraphicAnimationKey(10, 2), out _));
    }

    [Fact]
    public void Create_OneFrameMayBelongToMultipleAnimations()
    {
        GraphicAssetCatalog catalog = CreateCatalog();

        Assert.Equal(new[] { new GraphicAnimationKey(10, 1), new GraphicAnimationKey(30, 1) },
            catalog.GetAnimations(new SpriteReference(10, 1)).Select(animation => animation.Key).ToArray());
        Assert.Equal(new[] { new GraphicAnimationKey(10, 1) },
            catalog.GetAnimations(new SpriteReference(10, 2)).Select(animation => animation.Key).ToArray());
        Assert.Equal(new[] { new GraphicAnimationKey(30, 2) },
            catalog.GetAnimations(new SpriteReference(30, 3)).Select(animation => animation.Key).ToArray());
    }

    [Fact]
    public void Create_SheetWithTwoMappingsOfSameCategoryAppearsOnceInGetSheets()
    {
        string json = """
            { "version": 1,
              "sheets": { "10": { "categories": [ { "name": "Helm", "id": 126 }, { "name": "Helm", "id": 127 } ] } },
              "animations": [ { "ownerSheet": 10, "id": 1, "fps": 8, "frames": [[10, 1]] } ] }
            """;

        GraphicAssetCatalog catalog = GraphicAssetCatalog.Create(
            SpriteManifest.Parse(SpriteJson), GraphicAnimationManifest.Parse(json));

        Assert.Equal(new[] { 10 }, catalog.GetSheets(GraphicCategory.Helm));
        Assert.Equal(new[]
        {
            new GraphicCategoryMapping(GraphicCategory.Helm, 126),
            new GraphicCategoryMapping(GraphicCategory.Helm, 127)
        }, catalog.GetMappings(10));
    }

    [Fact]
    public void Create_MissingCategorizedSheetIsRejected()
    {
        string json = """
            { "version": 1, "sheets": { "99": { "categories": [ { "name": "Body", "id": 1 } ] } }, "animations": [] }
            """;

        GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(
            () => GraphicAssetCatalog.Create(SpriteManifest.Parse(SpriteJson), GraphicAnimationManifest.Parse(json)));

        Assert.Equal(GraphicAnimationManifestError.UnknownSheet, ex.Error);
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Create_MissingAnimationOwnerSheetIsRejected()
    {
        string json = """
            { "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 99, "id": 1, "fps": 8, "frames": [[10, 1]] } ] }
            """;

        GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(
            () => GraphicAssetCatalog.Create(SpriteManifest.Parse(SpriteJson), GraphicAnimationManifest.Parse(json)));

        Assert.Equal(GraphicAnimationManifestError.UnknownSheet, ex.Error);
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Create_MissingFrameReferenceIsRejected()
    {
        string json = """
            { "version": 1, "sheets": { "10": { "categories": [] } }, "animations": [ { "ownerSheet": 10, "id": 1, "fps": 8, "frames": [[10, 999]] } ] }
            """;

        GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(
            () => GraphicAssetCatalog.Create(SpriteManifest.Parse(SpriteJson), GraphicAnimationManifest.Parse(json)));

        Assert.Equal(GraphicAnimationManifestError.UnknownFrameReference, ex.Error);
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void Create_LateInvalidFrameAfterValidEntriesRejectsConstruction()
    {
        string json = """
            { "version": 1,
              "sheets": { "10": { "categories": [ { "name": "Body", "id": 1 } ] } },
              "animations": [
                { "ownerSheet": 10, "id": 1, "fps": 8, "frames": [[10, 1]] },
                { "ownerSheet": 10, "id": 2, "fps": 8, "frames": [[10, 1], [10, 999]] }
              ] }
            """;

        GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(
            () => GraphicAssetCatalog.Create(SpriteManifest.Parse(SpriteJson), GraphicAnimationManifest.Parse(json)));

        Assert.Equal(GraphicAnimationManifestError.UnknownFrameReference, ex.Error);
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void Create_PublishedCollectionsCannotBeMutatedOrCastToMutableBackingCollections()
    {
        GraphicAssetCatalog catalog = CreateCatalog();

        AssertCannotMutate(catalog.GetSheets(null));
        AssertCannotMutate(catalog.GetSheets(GraphicCategory.Body));
        AssertCannotMutate(catalog.GetMappings(30));
        AssertCannotMutate(catalog.GetAnimations(new SpriteReference(10, 1)));
        Assert.False(catalog.GetSheets(GraphicCategory.Eyes) is List<int>);
    }

    [Fact]
    public void Load_ComposesSpriteAndAnimationManifestsFromAssetDirectory()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "manifest.json"), SpriteJson);
            File.WriteAllText(Path.Combine(root, "animation-manifest.json"), AnimationJson);

            GraphicAssetCatalog catalog = GraphicAssetCatalog.Load(root);

            Assert.Equal(new[] { 10, 20, 30 }, catalog.GetSheets(null));
            Assert.Equal(new[] { 10, 30 }, catalog.GetSheets(GraphicCategory.Body));
            Assert.True(catalog.TryGetAnimation(new GraphicAnimationKey(30, 1), out GraphicAnimation animation));
            Assert.Equal(2, animation.Frames.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingSidecarPreservesTypedDiagnostics()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "manifest.json"), SpriteJson);

            GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(() => GraphicAssetCatalog.Load(root));
            Assert.Equal(GraphicAnimationManifestError.ManifestNotFound, ex.Error);
            Assert.Equal(Path.Combine(root, "animation-manifest.json"), ex.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingSpriteManifestPreservesTypedDiagnostics()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "animation-manifest.json"), AnimationJson);

            SpriteManifestException ex = Assert.Throws<SpriteManifestException>(() => GraphicAssetCatalog.Load(root));
            Assert.Equal(SpriteManifestError.ManifestNotFound, ex.Error);
            Assert.Equal(Path.Combine(root, "manifest.json"), ex.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static GraphicAssetCatalog CreateCatalog()
        => GraphicAssetCatalog.Create(SpriteManifest.Parse(SpriteJson), GraphicAnimationManifest.Parse(AnimationJson));

    private static void AssertCannotMutate<T>(IReadOnlyList<T> view)
    {
        Assert.False(view is T[]);
        Assert.False(view is List<T>);
        Assert.Throws<InvalidCastException>(() =>
        {
            var _ = (List<T>)view;
        });

        if (view is IList<T> mutable)
        {
            Assert.Throws<NotSupportedException>(() => mutable[0] = default!);
        }
    }

    private static string CreateTempRoot()
        => Path.Combine(Path.GetTempPath(), "graphic-asset-catalog-tests-" + Guid.NewGuid().ToString("N"));
}
