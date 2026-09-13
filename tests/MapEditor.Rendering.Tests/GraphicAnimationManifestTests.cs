using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class GraphicAnimationManifestTests
{
    private const string ValidJson = """
        { "version": 1,
          "sheets": {
            "115": { "categories": [ { "name": "Body", "id": 1 }, { "name": "Tiles" } ] },
            "100": { "categories": [ { "name": "Spells" } ] }
          },
          "animations": [
            { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [[115, 3205], [115, 3206]] },
            { "ownerSheet": 100, "id": 3205, "fps": 8, "frames": [[100, 7]] }
          ] }
        """;

    [Fact]
    public void Parse_ValidDocumentExposesSheetsCategoriesAndAnimations()
    {
        GraphicAnimationManifest manifest = GraphicAnimationManifest.Parse(ValidJson);

        Assert.Equal(1, manifest.Version);
        Assert.Equal(new[] { 100, 115 }, manifest.SheetIds);
        Assert.True(manifest.ContainsSheet(115));
        Assert.True(manifest.ContainsSheet(100));
        Assert.False(manifest.ContainsSheet(999));

        Assert.Equal(new[]
        {
            new GraphicCategoryMapping(GraphicCategory.Body, 1),
            new GraphicCategoryMapping(GraphicCategory.Tiles, null)
        }, manifest.GetCategories(115));
        Assert.Equal(new[] { new GraphicCategoryMapping(GraphicCategory.Spells, null) }, manifest.GetCategories(100));
        Assert.Empty(manifest.GetCategories(999));

        Assert.Equal(2, manifest.Animations.Count);
        GraphicAnimation animation = manifest.Animations[0];
        Assert.Equal(new GraphicAnimationKey(100, 3205), animation.Key);
        Assert.Equal(8, animation.FramesPerSecond);
        Assert.Equal(new[] { new SpriteReference(100, 7) }, animation.Frames);
        Assert.Equal(new GraphicAnimationKey(115, 3205), manifest.Animations[1].Key);
        Assert.Equal(new[] { new SpriteReference(115, 3205), new SpriteReference(115, 3206) }, manifest.Animations[1].Frames);

        Assert.True(manifest.TryGetAnimation(new GraphicAnimationKey(115, 3205), out GraphicAnimation found));
        Assert.Equal(8, found.FramesPerSecond);
        Assert.True(manifest.TryGetAnimation(new GraphicAnimationKey(100, 3205), out _));
        Assert.False(manifest.TryGetAnimation(new GraphicAnimationKey(115, 3206), out _));
    }

    [Fact]
    public void Parse_ItemTilesCategoryParsesWithoutId()
    {
        string json = """
            { "version": 1,
              "sheets": { "115": { "categories": [ { "name": "ItemTiles" } ] } },
              "animations": [] }
            """;

        GraphicAnimationManifest manifest = GraphicAnimationManifest.Parse(json);

        Assert.Equal(new[] { new GraphicCategoryMapping(GraphicCategory.ItemTiles, null) }, manifest.GetCategories(115));
    }

    [Fact]
    public void Parse_BodyAndItemTilesParseBothAndSortBodyFirst()
    {
        string bodyFirst = """
            { "version": 1,
              "sheets": { "115": { "categories": [ { "name": "Body", "id": 1 }, { "name": "ItemTiles" } ] } },
              "animations": [] }
            """;
        string itemTilesFirst = """
            { "version": 1,
              "sheets": { "115": { "categories": [ { "name": "ItemTiles" }, { "name": "Body", "id": 1 } ] } },
              "animations": [] }
            """;

        foreach (string json in new[] { bodyFirst, itemTilesFirst })
        {
            GraphicAnimationManifest manifest = GraphicAnimationManifest.Parse(json);

            Assert.Equal(new[]
            {
                new GraphicCategoryMapping(GraphicCategory.Body, 1),
                new GraphicCategoryMapping(GraphicCategory.ItemTiles, null)
            }, manifest.GetCategories(115));
        }
    }

    [Fact]
    public void Parse_SameAnimationIdOnDifferentOwnersResolvesCompositeKeys()
    {
        GraphicAnimationManifest manifest = GraphicAnimationManifest.Parse(ValidJson);

        Assert.True(manifest.TryGetAnimation(new GraphicAnimationKey(100, 3205), out GraphicAnimation hundred));
        Assert.True(manifest.TryGetAnimation(new GraphicAnimationKey(115, 3205), out GraphicAnimation oneFifteen));
        Assert.Equal(new[] { new SpriteReference(100, 7) }, hundred.Frames);
        Assert.Equal(2, oneFifteen.Frames.Count);
    }

    [Fact]
    public void Parse_DuplicateCompositeAnimationKeyIsRejected()
    {
        string json = """
            { "version": 1, "sheets": {}, "animations": [
              { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [[115, 1]] },
              { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [[115, 2]] }
            ] }
            """;

        AssertParseError(json, GraphicAnimationManifestError.DuplicateAnimation, "3205");
    }

    [Fact]
    public void Parse_DuplicateNormalizedSheetKeysAreRejected()
    {
        string json = """
            { "version": 1, "sheets": { "1": { "categories": [] }, "01": { "categories": [] } }, "animations": [] }
            """;

        AssertParseError(json, GraphicAnimationManifestError.DuplicateSheetId, "01");
    }

    [Fact]
    public void Parse_SortsSheetsAndAnimationsNumerically()
    {
        string json = """
            { "version": 1,
              "sheets": { "30": { "categories": [] }, "10": { "categories": [] }, "20": { "categories": [] } },
              "animations": [
                { "ownerSheet": 30, "id": 5, "fps": 8, "frames": [[30, 5]] },
                { "ownerSheet": 10, "id": 9, "fps": 8, "frames": [[10, 9]] },
                { "ownerSheet": 20, "id": 4, "fps": 8, "frames": [[20, 4]] },
                { "ownerSheet": 10, "id": 2, "fps": 8, "frames": [[10, 2]] }
              ] }
            """;

        GraphicAnimationManifest manifest = GraphicAnimationManifest.Parse(json);

        Assert.Equal(new[] { 10, 20, 30 }, manifest.SheetIds);
        Assert.Equal(new[]
        {
            new GraphicAnimationKey(10, 2),
            new GraphicAnimationKey(10, 9),
            new GraphicAnimationKey(20, 4),
            new GraphicAnimationKey(30, 5)
        }, manifest.Animations.Select(animation => animation.Key).ToArray());
    }

    [Fact]
    public void Parse_PublishedCollectionsCannotBeMutatedOrCastToMutableBackingCollections()
    {
        GraphicAnimationManifest manifest = GraphicAnimationManifest.Parse(ValidJson);

        AssertCannotMutate(manifest.SheetIds);
        AssertCannotMutate(manifest.Animations);
        AssertCannotMutate(manifest.GetCategories(115));
        AssertCannotMutate(manifest.Animations[0].Frames);
        Assert.False(manifest.GetCategories(999) is List<GraphicCategoryMapping>);
        Assert.Empty(manifest.GetCategories(999));
    }

    [Theory]
    [InlineData("""{ "sheets": {}, "animations": [] }""")]
    [InlineData("""{ "version": "1", "sheets": {}, "animations": [] }""")]
    [InlineData("""{ "version": 1.5, "sheets": {}, "animations": [] }""")]
    [InlineData("""{ "version": 2, "sheets": {}, "animations": [] }""")]
    public void Parse_VersionMatrixIsRejected(string json)
    {
        AssertParseError(json, GraphicAnimationManifestError.UnsupportedVersion, "version");
    }

    [Theory]
    [InlineData("""{ "version": 1""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [] } trailing""")]
    public void Parse_MalformedJsonIsRejected(string json)
    {
        AssertParseError(json, GraphicAnimationManifestError.MalformedJson, "not valid JSON");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""[1, 2]""")]
    [InlineData("""{ "version": 1, "sheets": {}, "sheets": {}, "animations": [] }""")]
    [InlineData("""{ "version": 1, "version": 1, "sheets": {}, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [], "animations": [] }""")]
    public void Parse_InvalidRootIsRejected(string json)
    {
        AssertError(json, GraphicAnimationManifestError.InvalidRoot);
    }

    [Theory]
    [InlineData("""{ "version": 1, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": null, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": [], "animations": [] }""")]
    public void Parse_MissingSheetsIsRejected(string json)
    {
        AssertParseError(json, GraphicAnimationManifestError.MissingSheets, "sheets");
    }

    [Theory]
    [InlineData("""{ "version": 1, "sheets": {} }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": null }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": {} }""")]
    public void Parse_MissingAnimationsIsRejected(string json)
    {
        AssertParseError(json, GraphicAnimationManifestError.MissingAnimations, "animations");
    }

    [Theory]
    [InlineData("""{ "version": 1, "sheets": { "-": { "categories": [] } }, "animations": [] }""", "-")]
    [InlineData("""{ "version": 1, "sheets": { " 1": { "categories": [] } }, "animations": [] }""", " 1")]
    [InlineData("""{ "version": 1, "sheets": { "1e0": { "categories": [] } }, "animations": [] }""", "1e0")]
    [InlineData("""{ "version": 1, "sheets": { "99999999999": { "categories": [] } }, "animations": [] }""", "99999999999")]
    public void Parse_InvalidSheetKeysAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, GraphicAnimationManifestError.InvalidSheetId, messageFragment);
    }

    [Theory]
    [InlineData("""{ "version": 1, "sheets": { "1": null }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": [] }, "animations": [] }""")]
    public void Parse_InvalidSheetEntriesAreRejected(string json)
    {
        AssertParseError(json, GraphicAnimationManifestError.InvalidSheetEntry, "1");
    }

    [Theory]
    [InlineData("""{ "version": 1, "sheets": { "1": {} }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": null } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": {} } }, "animations": [] }""")]
    public void Parse_MissingCategoriesAreRejected(string json)
    {
        AssertParseError(json, GraphicAnimationManifestError.MissingCategories, "categories");
    }

    [Theory]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [], "categories": [] } }, "animations": [] }""")]
    public void Parse_InvalidCategoriesAreRejected(string json)
    {
        AssertParseError(json, GraphicAnimationManifestError.InvalidCategories, "categories");
    }

    [Theory]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "All", "id": 1 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Wings", "id": 1 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "body", "id": 1 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "id": 1 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": 1 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Body", "id": 1, "name": "Body" } ] } }, "animations": [] }""")]
    public void Parse_InvalidCategoryNamesAreRejected(string json)
    {
        AssertParseError(json, GraphicAnimationManifestError.InvalidCategoryName, "name");
    }

    [Theory]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Body" } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Hand", "id": "1" } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Body", "id": 1.5 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Tiles", "id": 1 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Spells", "id": 2 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Tiles", "id": null } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Spells", "id": "x" } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "ItemTiles", "id": 1 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Body", "id": 1, "id": 1 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ "Body" ] } }, "animations": [] }""")]
    public void Parse_CategoryIdPresenceRulesAreEnforced(string json)
    {
        AssertError(json, GraphicAnimationManifestError.InvalidCategoryMapping);
    }

    [Theory]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Body", "id": 1 }, { "name": "Body", "id": 1 } ] } }, "animations": [] }""")]
    [InlineData("""{ "version": 1, "sheets": { "1": { "categories": [ { "name": "Tiles" }, { "name": "Tiles" } ] } }, "animations": [] }""")]
    public void Parse_DuplicateCategoryMappingsAreRejected(string json)
    {
        AssertError(json, GraphicAnimationManifestError.DuplicateCategoryMapping);
    }

    [Theory]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": "115", "id": 3205, "fps": 8, "frames": [[115, 1]] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205.5, "fps": 8, "frames": [[115, 1]] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 0, "frames": [[115, 1]] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": -2, "frames": [[115, 1]] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 8.5, "frames": [[115, 1]] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "frames": [[115, 1]] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "fps": 8, "frames": [[115, 1]] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 8 } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": {} } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "id": 3205, "fps": 8, "frames": [[115, 1]] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ 42 ] }""")]
    public void Parse_InvalidAnimationsAreRejected(string json)
    {
        AssertError(json, GraphicAnimationManifestError.InvalidAnimation);
    }

    [Theory]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [ [115] ] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [ [115, 1, 2] ] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [ ["115", 1] ] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [ [115.5, 1] ] } ] }""")]
    [InlineData("""{ "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [ { "sheet": 115 } ] } ] }""")]
    public void Parse_InvalidFramesAreRejected(string json)
    {
        AssertError(json, GraphicAnimationManifestError.InvalidFrame);
    }

    [Fact]
    public void Parse_FrameGraphicZeroIsRejected()
    {
        string json = """
            { "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [[115, 1], [115, 0]] } ] }
            """;

        AssertError(json, GraphicAnimationManifestError.EmptyFrameGraphic);
    }

    [Fact]
    public void Parse_EmptyAnimationFramesAreRejected()
    {
        string json = """
            { "version": 1, "sheets": {}, "animations": [ { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [] } ] }
            """;

        AssertError(json, GraphicAnimationManifestError.EmptyAnimationFrames);
    }

    [Fact]
    public void ParseFailure_LateInvalidEntryDoesNotPublishPartialManifest()
    {
        string json = """
            { "version": 1,
              "sheets": { "115": { "categories": [ { "name": "Body", "id": 1 } ] } },
              "animations": [
                { "ownerSheet": 115, "id": 3205, "fps": 8, "frames": [[115, 3205]] },
                { "ownerSheet": 115, "id": 3206, "fps": 0, "frames": [[115, 3206]] }
              ] }
            """;

        GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(() => GraphicAnimationManifest.Parse(json));

        Assert.Equal(GraphicAnimationManifestError.InvalidAnimation, ex.Error);
        Assert.Equal("<memory>", ex.Path);
    }

    [Fact]
    public void Parse_UnknownRootPropertiesAreAllowed()
    {
        GraphicAnimationManifest manifest = GraphicAnimationManifest.Parse("""{ "version": 1, "note": "forward-compatible", "sheets": {}, "animations": [] }""");

        Assert.Equal(1, manifest.Version);
        Assert.Empty(manifest.SheetIds);
        Assert.Empty(manifest.Animations);
    }

    [Fact]
    public void Load_ReadsAnimationManifestFromNormalizedAssetRoot()
    {
        string root = CreateTempRoot();
        try
        {
            string assetRoot = Path.Combine(root, "assets", "sprites");
            Directory.CreateDirectory(assetRoot);
            File.WriteAllText(Path.Combine(assetRoot, "animation-manifest.json"), ValidJson);

            string mixedPath = Path.Combine(root, "assets", ".", "sprites", "..", "sprites");
            GraphicAnimationManifest manifest = GraphicAnimationManifest.Load(mixedPath);

            Assert.Equal(new[] { 100, 115 }, manifest.SheetIds);
            Assert.Equal(2, manifest.Animations.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingManifestReportsManifestNotFoundWithFullPath()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(root);

            GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(() => GraphicAnimationManifest.Load(root));

            Assert.Equal(GraphicAnimationManifestError.ManifestNotFound, ex.Error);
            Assert.Equal(Path.Combine(root, "animation-manifest.json"), ex.Path);
            Assert.Contains("animation-manifest.json", ex.Message);
            Assert.Null(ex.InnerException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_DirectoryAtManifestPathReportsReadFailed()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "animation-manifest.json"));

            GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(() => GraphicAnimationManifest.Load(root));

            Assert.Equal(GraphicAnimationManifestError.ReadFailed, ex.Error);
            Assert.Equal(Path.Combine(root, "animation-manifest.json"), ex.Path);
            Assert.NotNull(ex.InnerException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GraphicAnimationManifestException_IsIOExceptionWithTypedErrorAndPreservedInnerException()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "animation-manifest.json"));

            GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(() => GraphicAnimationManifest.Load(root));

            Assert.IsAssignableFrom<IOException>(ex);
            Assert.Equal(GraphicAnimationManifestError.ReadFailed, ex.Error);
            Assert.Equal(Path.Combine(root, "animation-manifest.json"), ex.Path);
            Assert.NotNull(ex.InnerException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_NullOrBlankPathThrowsArgumentError()
    {
        Assert.Throws<ArgumentException>(() => GraphicAnimationManifest.Load(null!));
        Assert.Throws<ArgumentException>(() => GraphicAnimationManifest.Load("  "));
    }

    private static void AssertError(string json, GraphicAnimationManifestError error)
    {
        GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(() => GraphicAnimationManifest.Parse(json));
        Assert.Equal(error, ex.Error);
        Assert.Equal("<memory>", ex.Path);
    }

    private static void AssertParseError(string json, GraphicAnimationManifestError error, string messageFragment)
    {
        GraphicAnimationManifestException ex = Assert.Throws<GraphicAnimationManifestException>(() => GraphicAnimationManifest.Parse(json));
        Assert.Equal(error, ex.Error);
        Assert.Equal("<memory>", ex.Path);
        Assert.Contains(messageFragment, ex.Message);
    }

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
        => Path.Combine(Path.GetTempPath(), "graphic-animation-manifest-tests-" + Guid.NewGuid().ToString("N"));
}
