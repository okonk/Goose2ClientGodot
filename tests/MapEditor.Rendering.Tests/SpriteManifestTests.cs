using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class SpriteManifestTests
{
    private const string ConverterContractJson = """
        { "tileSize": 32, "sheets": { "1000": {
          "108760": [0, 0, 48, 64],
          "108761": [48, 0, 48, 64],
          "108762": [0, 64, 48, 64],
          "108763": [48, 64, 48, 64],
          "108764": [0, 128, 48, 64],
          "108765": [48, 128, 48, 64],
          "108766": [0, 192, 48, 64],
          "108767": [48, 192, 48, 64]
        } } }
        """;

    [Fact]
    public void Parse_ConverterContractIndexesKnownRectsAndTileSize()
    {
        SpriteManifest manifest = SpriteManifest.Parse(ConverterContractJson);

        Assert.Equal(32, manifest.TileSize);
        Assert.Equal(48, manifest.MaxFrameWidth);
        Assert.Equal(64, manifest.MaxFrameHeight);
        Assert.Equal(new[] { 1000 }, manifest.SheetIds);
        Assert.True(manifest.ContainsSheet(1000));
        Assert.False(manifest.ContainsSheet(1001));
        Assert.Equal(8, manifest.Frames.Count);

        IReadOnlyList<SpriteFrame> frames = manifest.GetFrames(1000);
        Assert.Equal(8, frames.Count);
        Assert.Equal(new SpriteFrame(new SpriteReference(1000, 108760), new SpriteSourceRect(0, 0, 48, 64)), frames[0]);
        Assert.Equal(new SpriteFrame(new SpriteReference(1000, 108763), new SpriteSourceRect(48, 64, 48, 64)), frames[3]);
        Assert.Equal(new SpriteFrame(new SpriteReference(1000, 108767), new SpriteSourceRect(48, 192, 48, 64)), frames[7]);

        Assert.True(manifest.TryGetSourceRect(new SpriteReference(1000, 108764), out SpriteSourceRect rect));
        Assert.Equal(new SpriteSourceRect(0, 128, 48, 64), rect);
    }

    [Fact]
    public void Parse_SortsSheetsAndFramesNumericallyForSelectors()
    {
        string json = """
            { "tileSize": 32, "sheets": { "30": { "9": [0, 0, 4, 4], "10": [4, 0, 4, 4], "2": [0, 4, 4, 4] }, "10": { "5": [0, 0, 4, 4] }, "20": {} } }
            """;

        SpriteManifest manifest = SpriteManifest.Parse(json);

        Assert.Equal(new[] { 10, 20, 30 }, manifest.SheetIds);
        Assert.Equal(new[]
        {
            new SpriteFrame(new SpriteReference(10, 5), new SpriteSourceRect(0, 0, 4, 4)),
            new SpriteFrame(new SpriteReference(30, 2), new SpriteSourceRect(0, 4, 4, 4)),
            new SpriteFrame(new SpriteReference(30, 9), new SpriteSourceRect(0, 0, 4, 4)),
            new SpriteFrame(new SpriteReference(30, 10), new SpriteSourceRect(4, 0, 4, 4))
        }, manifest.Frames);
        Assert.Equal(new[] { 2, 9, 10 }, manifest.GetFrames(30).Select(frame => frame.Reference.Graphic).ToArray());
    }

    [Fact]
    public void Parse_NegativeSheetAndGraphicIdsAreAcceptedAndSorted()
    {
        string json = """
            { "tileSize": 32, "sheets": { "53": { "7": [16, 0, 16, 16], "-197": [0, 0, 16, 16] }, "-2": { "1": [0, 0, 4, 4] } } }
            """;

        SpriteManifest manifest = SpriteManifest.Parse(json);

        Assert.Equal(new[] { -2, 53 }, manifest.SheetIds);
        Assert.Equal(new[]
        {
            new SpriteFrame(new SpriteReference(53, -197), new SpriteSourceRect(0, 0, 16, 16)),
            new SpriteFrame(new SpriteReference(53, 7), new SpriteSourceRect(16, 0, 16, 16))
        }, manifest.GetFrames(53));
        Assert.True(manifest.TryGetSourceRect(new SpriteReference(53, -197), out SpriteSourceRect rect));
        Assert.Equal(new SpriteSourceRect(0, 0, 16, 16), rect);
    }

    [Fact]
    public void Parse_ComputesMaximumFrameDimensionsWithTileSizeFloor()
    {
        SpriteManifest manifest = SpriteManifest.Parse("""{ "tileSize": 32, "sheets": { "1": { "2": [0, 0, 100, 10] } } }""");

        Assert.Equal(100, manifest.MaxFrameWidth);
        Assert.Equal(32, manifest.MaxFrameHeight);
    }

    [Fact]
    public void Parse_EmptySheetsIsValidAndUsesTileSizeMaxima()
    {
        SpriteManifest manifest = SpriteManifest.Parse("""{ "tileSize": 32, "sheets": {} }""");

        Assert.Empty(manifest.SheetIds);
        Assert.Empty(manifest.Frames);
        Assert.Equal(32, manifest.MaxFrameWidth);
        Assert.Equal(32, manifest.MaxFrameHeight);
        Assert.False(manifest.ContainsSheet(1));
    }

    [Fact]
    public void Parse_EmptyFrameObjectIsValidKnownSheetWithNoFrames()
    {
        SpriteManifest manifest = SpriteManifest.Parse("""{ "tileSize": 32, "sheets": { "5": {} } }""");

        Assert.Equal(new[] { 5 }, manifest.SheetIds);
        Assert.True(manifest.ContainsSheet(5));
        Assert.Empty(manifest.GetFrames(5));
        Assert.Empty(manifest.Frames);
        Assert.Equal(32, manifest.MaxFrameWidth);
        Assert.Equal(32, manifest.MaxFrameHeight);
    }

    [Fact]
    public void Parse_AllowsLargeCheckedFrameDimensionsWithoutAnInventedDimensionOrAreaCap()
    {
        SpriteManifest manifest = SpriteManifest.Parse("""{ "tileSize": 32, "sheets": { "1": { "2": [0, 0, 200000, 200000] } } }""");

        Assert.Equal(200000, manifest.MaxFrameWidth);
        Assert.Equal(200000, manifest.MaxFrameHeight);
        Assert.True(manifest.TryGetSourceRect(new SpriteReference(1, 2), out SpriteSourceRect rect));
        Assert.Equal(new SpriteSourceRect(0, 0, 200000, 200000), rect);
    }

    [Fact]
    public void Parse_AllowsUnknownRootProperties()
    {
        SpriteManifest manifest = SpriteManifest.Parse("""{ "version": 2, "tileSize": 32, "note": "forward-compatible", "sheets": {} }""");

        Assert.Equal(32, manifest.TileSize);
        Assert.Empty(manifest.SheetIds);
    }

    [Fact]
    public void TryGetSourceRect_DistinguishesKnownAndUnknownReferences()
    {
        SpriteManifest manifest = SpriteManifest.Parse(ConverterContractJson);

        Assert.True(manifest.TryGetSourceRect(new SpriteReference(1000, 108760), out SpriteSourceRect known));
        Assert.Equal(new SpriteSourceRect(0, 0, 48, 64), known);

        Assert.False(manifest.TryGetSourceRect(new SpriteReference(1000, 999), out _));
        Assert.False(manifest.TryGetSourceRect(new SpriteReference(1001, 108760), out _));
        Assert.False(manifest.TryGetSourceRect(new SpriteReference(0, 0), out _));
    }

    [Fact]
    public void GetFrames_UnknownSheetReturnsEmptyReadOnlyList()
    {
        SpriteManifest manifest = SpriteManifest.Parse(ConverterContractJson);

        IReadOnlyList<SpriteFrame> frames = manifest.GetFrames(999);

        Assert.Empty(frames);
        Assert.False(frames is List<SpriteFrame>);
    }

    [Fact]
    public void PublishedCollectionsCannotBeMutatedOrCastToMutableBackingCollections()
    {
        SpriteManifest manifest = SpriteManifest.Parse(ConverterContractJson);

        AssertCannotMutate(manifest.SheetIds);
        AssertCannotMutate(manifest.Frames);
        AssertCannotMutate(manifest.GetFrames(1000));
    }

    [Fact]
    public void Load_ReadsManifestFromNormalizedAssetRoot()
    {
        string root = CreateTempRoot();
        try
        {
            string assetRoot = Path.Combine(root, "assets", "sprites");
            Directory.CreateDirectory(assetRoot);
            File.WriteAllText(Path.Combine(assetRoot, "manifest.json"), ConverterContractJson);

            string mixedPath = Path.Combine(root, "assets", ".", "sprites", "..", "sprites");
            SpriteManifest manifest = SpriteManifest.Load(mixedPath);

            Assert.Equal(32, manifest.TileSize);
            Assert.Equal(new[] { 1000 }, manifest.SheetIds);
            Assert.Equal(8, manifest.Frames.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SpriteReference_GraphicZeroIsEmptyRegardlessOfSheet()
    {
        Assert.True(new SpriteReference(0, 0).IsEmpty);
        Assert.True(new SpriteReference(5, 0).IsEmpty);
        Assert.True(new SpriteReference(1000, 0).IsEmpty);
        Assert.False(new SpriteReference(0, 7).IsEmpty);
        Assert.False(new SpriteReference(1000, 7).IsEmpty);
    }

    [Fact]
    public void Load_MissingManifestReportsManifestNotFoundWithFullPath()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(root);

            SpriteManifestException ex = Assert.Throws<SpriteManifestException>(() => SpriteManifest.Load(root));

            Assert.Equal(SpriteManifestError.ManifestNotFound, ex.Error);
            Assert.Equal(Path.Combine(root, "manifest.json"), ex.Path);
            Assert.Contains("manifest.json", ex.Message);
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
            Directory.CreateDirectory(Path.Combine(root, "manifest.json"));

            SpriteManifestException ex = Assert.Throws<SpriteManifestException>(() => SpriteManifest.Load(root));

            Assert.Equal(SpriteManifestError.ReadFailed, ex.Error);
            Assert.Equal(Path.Combine(root, "manifest.json"), ex.Path);
            Assert.NotNull(ex.InnerException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SpriteManifestException_IsIOExceptionWithTypedErrorAndPreservedInnerException()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "manifest.json"));

            SpriteManifestException ex = Assert.Throws<SpriteManifestException>(() => SpriteManifest.Load(root));

            Assert.IsAssignableFrom<IOException>(ex);
            Assert.Equal(SpriteManifestError.ReadFailed, ex.Error);
            Assert.Equal(Path.Combine(root, "manifest.json"), ex.Path);
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
        Assert.Throws<ArgumentException>(() => SpriteManifest.Load(null!));
        Assert.Throws<ArgumentException>(() => SpriteManifest.Load("  "));
    }

    [Theory]
    [InlineData("""{ "tileSize": 32""")]
    [InlineData("""{ "tileSize": 32, "sheets": {} } trailing""")]
    public void Parse_MalformedJsonIsRejected(string json)
    {
        AssertParseError(json, SpriteManifestError.MalformedJson, "not valid JSON");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""[1, 2]""")]
    [InlineData("""{ "tileSize": 32, "tileSize": 32, "sheets": {} }""")]
    [InlineData("""{ "tileSize": 32, "sheets": {}, "sheets": {} }""")]
    public void Parse_InvalidRootIsRejected(string json)
    {
        SpriteManifestException ex = Assert.Throws<SpriteManifestException>(() => SpriteManifest.Parse(json));
        Assert.Equal(SpriteManifestError.InvalidRoot, ex.Error);
    }

    [Theory]
    [InlineData("""{ "sheets": {} }""", SpriteManifestError.MissingTileSize, "tileSize")]
    [InlineData("""{ "tileSize": "32", "sheets": {} }""", SpriteManifestError.MissingTileSize, "tileSize")]
    [InlineData("""{ "tileSize": 32.5, "sheets": {} }""", SpriteManifestError.MissingTileSize, "tileSize")]
    [InlineData("""{ "tileSize": 31, "sheets": {} }""", SpriteManifestError.UnsupportedTileSize, "31")]
    [InlineData("""{ "tileSize": 64, "sheets": {} }""", SpriteManifestError.UnsupportedTileSize, "64")]
    public void Parse_TileSizeMatrixIsRejected(string json, SpriteManifestError error, string messageFragment)
    {
        AssertParseError(json, error, messageFragment);
    }

    [Theory]
    [InlineData("""{ "tileSize": 32 }""")]
    [InlineData("""{ "tileSize": 32, "sheets": null }""")]
    [InlineData("""{ "tileSize": 32, "sheets": [] }""")]
    public void Parse_MissingSheetsIsRejected(string json)
    {
        AssertParseError(json, SpriteManifestError.MissingSheets, "sheets");
    }

    [Theory]
    [InlineData("""{ "tileSize": 32, "sheets": { "0": {} } }""", "0")]
    [InlineData("""{ "tileSize": 32, "sheets": { "-": {} } }""", "-")]
    [InlineData("""{ "tileSize": 32, "sheets": { "--1": {} } }""", "--1")]
    [InlineData("""{ "tileSize": 32, "sheets": { " 1": {} } }""", " 1")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1e0": {} } }""", "1e0")]
    [InlineData("""{ "tileSize": 32, "sheets": { "99999999999": {} } }""", "99999999999")]
    [InlineData("""{ "tileSize": 32, "sheets": { "-2147483649": {} } }""", "-2147483649")]
    public void Parse_InvalidSheetIdsAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, SpriteManifestError.InvalidSheetId, messageFragment);
    }

    [Theory]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": {}, "1": {} } }""", "1")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": {}, "01": {} } }""", "01")]
    public void Parse_DuplicateSheetIdsAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, SpriteManifestError.DuplicateSheetId, messageFragment);
    }

    [Theory]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": null } }""", "1")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": [] } }""", "1")]
    public void Parse_InvalidSheetFramesAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, SpriteManifestError.InvalidSheetFrames, messageFragment);
    }

    [Theory]
    [InlineData("""{ "tileSize": 32, "sheets": { "5": { "0": [0, 0, 4, 4] } } }""", "0")]
    [InlineData("""{ "tileSize": 32, "sheets": { "5": { "-": [0, 0, 4, 4] } } }""", "-")]
    [InlineData("""{ "tileSize": 32, "sheets": { "5": { "--1": [0, 0, 4, 4] } } }""", "--1")]
    [InlineData("""{ "tileSize": 32, "sheets": { "5": { " 1": [0, 0, 4, 4] } } }""", " 1")]
    [InlineData("""{ "tileSize": 32, "sheets": { "5": { "1e0": [0, 0, 4, 4] } } }""", "1e0")]
    [InlineData("""{ "tileSize": 32, "sheets": { "5": { "99999999999": [0, 0, 4, 4] } } }""", "99999999999")]
    [InlineData("""{ "tileSize": 32, "sheets": { "5": { "-2147483649": [0, 0, 4, 4] } } }""", "-2147483649")]
    public void Parse_InvalidGraphicIdsAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, SpriteManifestError.InvalidGraphicId, messageFragment);
    }

    [Theory]
    [InlineData("""{ "tileSize": 32, "sheets": { "5": { "2": [0, 0, 4, 4], "2": [0, 0, 4, 4] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "5": { "2": [0, 0, 4, 4], "02": [0, 0, 4, 4] } } }""", "02")]
    public void Parse_DuplicateGraphicIdsAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, SpriteManifestError.DuplicateGraphicId, messageFragment);
    }

    [Theory]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": null } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": {} } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": "x" } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": [] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": [0, 0, 4] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": [0, 0, 4, 4, 0] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": [1.5, 0, 4, 4] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": ["0", 0, 4, 4] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": [-1, 0, 4, 4] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": [0, -1, 4, 4] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": [0, 0, 0, 4] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": [0, 0, 4, -4] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": [2147483634, 0, 20, 4] } } }""", "2")]
    [InlineData("""{ "tileSize": 32, "sheets": { "1": { "2": [0, 2147483634, 4, 20] } } }""", "2")]
    public void Parse_InvalidFrameRectsAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, SpriteManifestError.InvalidFrameRect, messageFragment);
    }

    [Fact]
    public void ParseFailure_DoesNotPublishPartialManifest()
    {
        string partiallyValid = """{ "tileSize": 32, "sheets": { "1": { "2": [0, 0, 4, 4], "bogus": [0, 0, 4, 4] } } }""";

        SpriteManifestException ex = Assert.Throws<SpriteManifestException>(() => SpriteManifest.Parse(partiallyValid));

        Assert.Equal(SpriteManifestError.InvalidGraphicId, ex.Error);
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

    private static void AssertParseError(string json, SpriteManifestError error, string messageFragment)
    {
        SpriteManifestException ex = Assert.Throws<SpriteManifestException>(() => SpriteManifest.Parse(json));
        Assert.Equal(error, ex.Error);
        Assert.Equal("<memory>", ex.Path);
        Assert.Contains(messageFragment, ex.Message);
    }

    private static string CreateTempRoot()
        => Path.Combine(Path.GetTempPath(), "sprite-manifest-tests-" + Guid.NewGuid().ToString("N"));
}
