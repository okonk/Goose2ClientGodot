using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class TerrainAssetCatalogTests
{
    private const string EmptyFileHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static readonly Guid GrassId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DirtId = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid WaterId = new("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void Revision_Missing_IsFalseWithEmptyHash()
    {
        var revision = TerrainFileRevision.Missing;

        Assert.False(revision.Exists);
        Assert.Equal(string.Empty, revision.ContentHash);
    }

    [Fact]
    public void Revision_FromBytes_EmptyFile_HasEmptySha256()
    {
        var revision = TerrainFileRevision.FromBytes(Array.Empty<byte>());

        Assert.True(revision.Exists);
        Assert.Equal(EmptyFileHash, revision.ContentHash);
    }

    [Fact]
    public void Revision_FromBytes_KnownContent_IsLowercaseHexSha256()
    {
        var revision = TerrainFileRevision.FromBytes(Encoding.UTF8.GetBytes("abc"));

        Assert.True(revision.Exists);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", revision.ContentHash);
    }

    [Fact]
    public void Validate_ValidCatalogAndManifest_PublishesIndex()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");

        var result = TerrainAssetCatalog.Validate(catalog, manifest);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Index);
        Assert.DoesNotContain(result.Issues, issue => issue.Severity == TerrainValidationSeverity.Error);
    }

    [Theory]
    [InlineData("missing", "{ \"1\": { } }")]
    [InlineData("oversized", "{ \"1\": { \"10\": [0, 0, 48, 64] } }")]
    public void Validate_MissingOrOversizedGraphic_ReturnsNoIndex(string kind, string sheets)
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest(sheets);

        var result = TerrainAssetCatalog.Validate(catalog, manifest);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        var issue = Assert.Single(result.Issues, issue => issue.Severity == TerrainValidationSeverity.Error);
        Assert.Equal(
            kind == "missing" ? TerrainValidationCode.MissingSpriteFrame : TerrainValidationCode.SpriteFrameSizeMismatch,
            issue.Code);
        Assert.Equal(new TerrainGraphicReference(1, 10), issue.GraphicReference);
    }

    [Fact]
    public void Validate_UsesManifestWithoutSpriteResolution()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "manifest.json"),
                "{ \"tileSize\": 32, \"sheets\": { \"1\": { \"10\": [0, 0, 32, 32] } } }");
            var manifest = SpriteManifest.Load(directory);

            var result = TerrainAssetCatalog.Validate(CreateCatalog((GrassId, "Grass", 1, 10)), manifest);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Index);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Validate_GraphicZeroDeclaredInManifest_CannotPublish()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 0));
        var manifest = CreateManifest("{ \"1\": { \"0\": [0, 0, 32, 32] } }");

        var result = TerrainAssetCatalog.Validate(catalog, manifest);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == TerrainValidationCode.InvalidGraphicNumber
                && issue.GraphicReference == new TerrainGraphicReference(1, 0));
    }

    [Fact]
    public void Validate_MergesCoreAndManifestIssuesInDeterministicOrder()
    {
        var catalog = CreateMergedOrderCatalog();
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] }, \"3\": { \"30\": [0, 0, 48, 64] } }");

        var result = TerrainAssetCatalog.Validate(catalog, manifest);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);

        var errors = result.Issues.Where(issue => issue.Severity == TerrainValidationSeverity.Error).ToList();
        Assert.Equal(
            new[]
            {
                TerrainValidationCode.DuplicateTerrainName,
                TerrainValidationCode.DuplicateTerrainName,
                TerrainValidationCode.MissingCenteredGraphic,
                TerrainValidationCode.MissingSpriteFrame,
                TerrainValidationCode.SpriteFrameSizeMismatch
            },
            errors.Select(issue => issue.Code));
        Assert.Equal(GrassId, errors[0].TerrainId);
        Assert.Equal(WaterId, errors[1].TerrainId);
        Assert.Equal(WaterId, errors[2].TerrainId);
        Assert.Equal(new TerrainGraphicReference(2, 20), errors[3].GraphicReference);
        Assert.Equal(new TerrainGraphicReference(3, 30), errors[4].GraphicReference);

        var lastErrorIndex = result.Issues.ToList().FindLastIndex(issue => issue.Severity == TerrainValidationSeverity.Error);
        Assert.All(result.Issues.Skip(lastErrorIndex + 1), issue => Assert.Equal(TerrainValidationSeverity.Warning, issue.Severity));
    }

    [Fact]
    public void Load_Valid_ReturnsPublishableResultWithRevision()
    {
        var directory = CreateTempDirectory();
        try
        {
            var catalog = CreateCatalog((GrassId, "Grass", 1, 10), (DirtId, "Dirt", 1, 20));
            var bytes = Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(catalog));
            var sourcePath = Path.Combine(directory, "terrain-brushes.json");
            File.WriteAllBytes(sourcePath, bytes);
            var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] } }");

            var result = TerrainAssetCatalog.Load(directory, manifest);

            Assert.True(result.IsValid);
            Assert.True(result.CanAuthor);
            Assert.True(result.CanPaint);
            Assert.Null(result.Diagnostic);
            Assert.Equal(Path.GetFullPath(sourcePath), result.SourcePath);
            Assert.True(result.Revision.Exists);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Revision.ContentHash);
            Assert.Equal(catalog, result.Catalog);
            Assert.NotNull(result.Index);
            Assert.True(result.Index.TryGetGraphic(new TerrainGraphicReference(1, 10), out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_Missing_ReturnsValidEmpty()
    {
        var directory = CreateTempDirectory();
        try
        {
            var result = TerrainAssetCatalog.Load(directory, CreateManifest("{}"));

            Assert.True(result.IsValid);
            Assert.True(result.CanAuthor);
            Assert.False(result.CanPaint);
            Assert.Null(result.Catalog);
            Assert.Null(result.Index);
            Assert.Empty(result.Issues);
            Assert.Null(result.Diagnostic);
            Assert.False(result.Revision.Exists);
            Assert.Equal(string.Empty, result.Revision.ContentHash);
            Assert.Equal(Path.Combine(Path.GetFullPath(directory), "terrain-brushes.json"), result.SourcePath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_MissingAssetRoot_ReturnsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"));

        var result = TerrainAssetCatalog.Load(root, CreateManifest("{}"));

        Assert.False(result.IsValid);
        Assert.False(result.CanAuthor);
        Assert.False(result.CanPaint);
        Assert.NotNull(result.Diagnostic);
        Assert.Contains(root, result.Diagnostic);
        Assert.False(result.Revision.Exists);
    }

    [Fact]
    public void Load_FilePathIsDirectory_ReturnsInvalid()
    {
        var directory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "terrain-brushes.json"));

            var result = TerrainAssetCatalog.Load(directory, CreateManifest("{}"));

            Assert.False(result.IsValid);
            Assert.True(result.CanAuthor);
            Assert.False(result.CanPaint);
            Assert.Null(result.Catalog);
            Assert.Null(result.Index);
            Assert.False(result.Revision.Exists);
            Assert.NotNull(result.Diagnostic);
            Assert.Contains("terrain-brushes.json", result.Diagnostic);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Load_Unreadable_ReturnsInvalid()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "terrain-brushes.json");
        File.WriteAllText(path, "x");
        File.SetUnixFileMode(path, 0);
        try
        {
            var result = TerrainAssetCatalog.Load(directory, CreateManifest("{}"));

            Assert.False(result.IsValid);
            Assert.True(result.CanAuthor);
            Assert.False(result.CanPaint);
            Assert.False(result.Revision.Exists);
            Assert.NotNull(result.Diagnostic);
            Assert.Contains(path, result.Diagnostic);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_Malformed_ReturnsInvalidWithRawRevision()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "terrain-brushes.json");
            var bytes = Encoding.UTF8.GetBytes("{ \"version\": 1,");
            File.WriteAllBytes(sourcePath, bytes);

            var result = TerrainAssetCatalog.Load(directory, CreateManifest("{}"));

            Assert.False(result.IsValid);
            Assert.True(result.CanAuthor);
            Assert.False(result.CanPaint);
            Assert.Null(result.Catalog);
            Assert.Null(result.Index);
            Assert.Empty(result.Issues);
            Assert.True(result.Revision.Exists);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Revision.ContentHash);
            Assert.NotNull(result.Diagnostic);
            Assert.Contains(sourcePath, result.Diagnostic);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_EmptyFile_IsInvalidWithEmptyFileRevision()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "terrain-brushes.json"), "");

            var result = TerrainAssetCatalog.Load(directory, CreateManifest("{}"));

            Assert.False(result.IsValid);
            Assert.True(result.Revision.Exists);
            Assert.Equal(EmptyFileHash, result.Revision.ContentHash);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_ValidEmptyCatalog_CanAuthorButNotPaint()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "terrain-brushes.json"), "{ \"version\": 1, \"terrains\": [], \"graphics\": [] }");

            var result = TerrainAssetCatalog.Load(directory, CreateManifest("{}"));

            Assert.True(result.IsValid);
            Assert.True(result.CanAuthor);
            Assert.False(result.CanPaint);
            Assert.NotNull(result.Catalog);
            Assert.Empty(result.Catalog.Graphics);
            Assert.NotNull(result.Index);
            Assert.Null(result.Diagnostic);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_MissingFrame_ReturnsInvalidWithManifestIssue()
    {
        var directory = CreateTempDirectory();
        try
        {
            WriteCatalog(directory, CreateCatalog((GrassId, "Grass", 1, 10)));

            var result = TerrainAssetCatalog.Load(directory, CreateManifest("{}"));

            Assert.False(result.IsValid);
            Assert.Null(result.Index);
            Assert.True(result.Revision.Exists);
            Assert.Contains(result.Issues, issue =>
                issue.Code == TerrainValidationCode.MissingSpriteFrame
                && issue.GraphicReference == new TerrainGraphicReference(1, 10));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_NonTileSizeFrame_ReturnsInvalidWithSizeIssue()
    {
        var directory = CreateTempDirectory();
        try
        {
            WriteCatalog(directory, CreateCatalog((GrassId, "Grass", 1, 10)));

            var result = TerrainAssetCatalog.Load(directory, CreateManifest("{ \"1\": { \"10\": [0, 0, 16, 32] } }"));

            Assert.False(result.IsValid);
            Assert.Null(result.Index);
            Assert.Contains(result.Issues, issue =>
                issue.Code == TerrainValidationCode.SpriteFrameSizeMismatch
                && issue.GraphicReference == new TerrainGraphicReference(1, 10));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_MergesSemanticAndManifestIssuesInDeterministicOrder()
    {
        var directory = CreateTempDirectory();
        try
        {
            WriteCatalog(directory, CreateMergedOrderCatalog());
            var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] }, \"3\": { \"30\": [0, 0, 48, 64] } }");

            var result = TerrainAssetCatalog.Load(directory, manifest);

            Assert.False(result.IsValid);
            Assert.Null(result.Index);
            var errors = result.Issues.Where(issue => issue.Severity == TerrainValidationSeverity.Error).ToList();
            Assert.Equal(
                new[]
                {
                    TerrainValidationCode.DuplicateTerrainName,
                    TerrainValidationCode.DuplicateTerrainName,
                    TerrainValidationCode.MissingCenteredGraphic,
                    TerrainValidationCode.MissingSpriteFrame,
                    TerrainValidationCode.SpriteFrameSizeMismatch
                },
                errors.Select(issue => issue.Code));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static TerrainCatalog CreateMergedOrderCatalog()
    {
        return new TerrainCatalog(
            new[]
            {
                new TerrainDefinition(GrassId, "Grass", null),
                new TerrainDefinition(DirtId, "Dirt", null),
                new TerrainDefinition(WaterId, "Grass", null)
            },
            new[]
            {
                new TerrainGraphicDefinition(new TerrainGraphicReference(1, 10), new TerrainPattern(Center: GrassId)),
                new TerrainGraphicDefinition(new TerrainGraphicReference(3, 30), new TerrainPattern(Center: DirtId)),
                new TerrainGraphicDefinition(new TerrainGraphicReference(2, 20), new TerrainPattern(Center: GrassId))
            });
    }

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

    private static SpriteManifest CreateManifest(string sheetsJson)
        => SpriteManifest.Parse($"{{ \"tileSize\": 32, \"sheets\": {sheetsJson} }}");

    private static void WriteCatalog(string directory, TerrainCatalog catalog)
        => File.WriteAllText(
            Path.Combine(directory, "terrain-brushes.json"),
            TerrainCatalogJson.Serialize(catalog));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "terrain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
