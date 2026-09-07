using System;
using System.IO;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class AppearanceManifestTests
{
    private const string ConverterContractJson = """
        { "version": 1, "parts": {
          "Body": { "1": { "noEquip": [1000, 108760], "equip": [1001, 108900] } },
          "Hair": {}, "Eyes": {}, "Chest": {}, "Helm": {}, "Legs": {}, "Feet": {}, "Hand": {}
        } }
        """;

    [Fact]
    public void Parse_ConverterContractResolvesVariants()
    {
        AppearanceManifest manifest = AppearanceManifest.Parse(ConverterContractJson);

        Assert.True(manifest.TryGetReference(AppearancePartKind.Body, 1, 3, out SpriteReference noEquip));
        Assert.Equal(new SpriteReference(1000, 108760), noEquip);

        Assert.True(manifest.TryGetReference(AppearancePartKind.Body, 1, 0, out SpriteReference equip));
        Assert.Equal(new SpriteReference(1001, 108900), equip);

        Assert.False(manifest.TryGetReference(AppearancePartKind.Hair, 1, 3, out _));
        Assert.False(manifest.TryGetReference(AppearancePartKind.Body, 99, 3, out _));
    }

    [Fact]
    public void Parse_IdleBodyStatePrefersNoEquip()
    {
        AppearanceManifest manifest = AppearanceManifest.Parse(ConverterContractJson);

        Assert.True(manifest.TryGetReference(AppearancePartKind.Body, 1, 3, out SpriteReference reference));

        Assert.Equal(new SpriteReference(1000, 108760), reference);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(-1)]
    public void Parse_NonIdleBodyStatesPreferEquip(int bodyState)
    {
        AppearanceManifest manifest = AppearanceManifest.Parse(ConverterContractJson);

        Assert.True(manifest.TryGetReference(AppearancePartKind.Body, 1, bodyState, out SpriteReference reference));

        Assert.Equal(new SpriteReference(1001, 108900), reference);
    }

    [Fact]
    public void Parse_VariantPropertyOrderDoesNotMatter()
    {
        AppearanceManifest manifest = AppearanceManifest.Parse(
            """{ "version": 1, "parts": { "Body": { "1": { "equip": [2, 20], "noEquip": [1, 10] } } } }""");

        Assert.True(manifest.TryGetReference(AppearancePartKind.Body, 1, 3, out SpriteReference noEquip));
        Assert.Equal(new SpriteReference(1, 10), noEquip);
        Assert.True(manifest.TryGetReference(AppearancePartKind.Body, 1, 0, out SpriteReference equip));
        Assert.Equal(new SpriteReference(2, 20), equip);
    }

    [Fact]
    public void Parse_MissingPreferredVariantFallsBackToTheOther()
    {
        AppearanceManifest manifest = AppearanceManifest.Parse(
            """{ "version": 1, "parts": { "Body": { "1": { "noEquip": [1, 10] }, "2": { "equip": [2, 20] } } } }""");

        Assert.True(manifest.TryGetReference(AppearancePartKind.Body, 1, 0, out SpriteReference noEquipOnly));
        Assert.Equal(new SpriteReference(1, 10), noEquipOnly);
        Assert.True(manifest.TryGetReference(AppearancePartKind.Body, 2, 3, out SpriteReference equipOnly));
        Assert.Equal(new SpriteReference(2, 20), equipOnly);
    }

    [Fact]
    public void Parse_AllowsUnknownRootProperties()
    {
        AppearanceManifest manifest = AppearanceManifest.Parse(
            """{ "source": "converter", "version": 1, "parts": {} }""");

        Assert.False(manifest.TryGetReference(AppearancePartKind.Body, 1, 3, out _));
    }

    [Theory]
    [InlineData("{", AppearanceManifestError.MalformedJson)]
    [InlineData("""{ "version": 1, "parts": {} } trailing""", AppearanceManifestError.MalformedJson)]
    [InlineData("null", AppearanceManifestError.InvalidRoot)]
    [InlineData("""[1, 2]""", AppearanceManifestError.InvalidRoot)]
    [InlineData("\"json\"", AppearanceManifestError.InvalidRoot)]
    [InlineData("""{ "parts": {} }""", AppearanceManifestError.MissingVersion)]
    [InlineData("""{ "version": "1", "parts": {} }""", AppearanceManifestError.MissingVersion)]
    [InlineData("""{ "version": 1.5, "parts": {} }""", AppearanceManifestError.MissingVersion)]
    [InlineData("""{ "version": 2, "parts": {} }""", AppearanceManifestError.UnsupportedVersion)]
    [InlineData("""{ "version": 0, "parts": {} }""", AppearanceManifestError.UnsupportedVersion)]
    [InlineData("""{ "version": 1, "version": 1, "parts": {} }""", AppearanceManifestError.InvalidRoot)]
    [InlineData("""{ "version": 1, "parts": {}, "parts": {} }""", AppearanceManifestError.InvalidRoot)]
    [InlineData("""{ "version": 1 }""", AppearanceManifestError.MissingParts)]
    [InlineData("""{ "version": 1, "parts": null }""", AppearanceManifestError.MissingParts)]
    [InlineData("""{ "version": 1, "parts": [] }""", AppearanceManifestError.MissingParts)]
    public void Parse_StructuralFailuresReportError(string json, AppearanceManifestError error)
    {
        AssertParseError(json, error);
    }

    [Theory]
    [InlineData("""{ "version": 1, "parts": { "Wings": {} } }""", "Wings")]
    [InlineData("""{ "version": 1, "parts": { "body": {} } }""", "body")]
    [InlineData("""{ "version": 1, "parts": { "BODY": {} } }""", "BODY")]
    [InlineData("""{ "version": 1, "parts": { "Body": [] } }""", "Body")]
    [InlineData("""{ "version": 1, "parts": { "0": {} } }""", "0")]
    public void Parse_InvalidPartKindsAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, AppearanceManifestError.InvalidPartKind, messageFragment);
    }

    [Fact]
    public void Parse_DuplicatePartKindIsRejected()
    {
        AssertParseError("""{ "version": 1, "parts": { "Body": {}, "Body": {} } }""", AppearanceManifestError.DuplicatePartKind, "Body");
    }

    [Theory]
    [InlineData("""{ "version": 1, "parts": { "Body": { "0": {} } } }""", "0")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "-1": {} } } }""", "-1")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1x": {} } } }""", "1x")]
    [InlineData("""{ "version": 1, "parts": { "Body": { " 1": {} } } }""", " 1")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1e0": {} } } }""", "1e0")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "99999999999": {} } } }""", "99999999999")]
    public void Parse_InvalidPartIdsAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, AppearanceManifestError.InvalidPartId, messageFragment);
    }

    [Theory]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": {}, "1": {} } } }""", "1")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": {}, "01": {} } } }""", "01")]
    public void Parse_DuplicatePartIdsAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, AppearanceManifestError.DuplicatePartId, messageFragment);
    }

    [Theory]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": null } } }""", "1")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": [] } } }""", "1")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": 5 } } }""", "1")]
    public void Parse_InvalidPartEntriesAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, AppearanceManifestError.InvalidPartEntry, messageFragment);
    }

    [Theory]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "cape": [1, 2] } } } }""", "cape")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "NoEquip": [1, 2] } } } }""", "NoEquip")]
    public void Parse_UnknownVariantPropertiesAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, AppearanceManifestError.InvalidVariant, messageFragment);
    }

    [Theory]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "noEquip": [1, 2], "noEquip": [3, 4] } } } }""", "noEquip")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "equip": [1, 2], "equip": [3, 4] } } } }""", "equip")]
    public void Parse_DuplicateVariantPropertiesAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, AppearanceManifestError.DuplicateVariant, messageFragment);
    }

    [Theory]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "noEquip": null } } } }""", "noEquip")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "noEquip": 5 } } } }""", "noEquip")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "noEquip": "x" } } } }""", "noEquip")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "noEquip": [] } } } }""", "noEquip")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "noEquip": [1] } } } }""", "noEquip")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "noEquip": [1, 2, 3] } } } }""", "noEquip")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "equip": [1.5, 2] } } } }""", "equip")]
    [InlineData("""{ "version": 1, "parts": { "Body": { "1": { "equip": ["1", 2] } } } }""", "equip")]
    public void Parse_InvalidReferencesAreRejected(string json, string messageFragment)
    {
        AssertParseError(json, AppearanceManifestError.InvalidReference, messageFragment);
    }

    [Fact]
    public void ParseFailure_ReportsExplicitSourcePath()
    {
        const string sourcePath = "C:/assets/appearance-manifest.json";

        AppearanceManifestException ex = Assert.Throws<AppearanceManifestException>(
            () => AppearanceManifest.Parse("{", sourcePath));

        Assert.Equal(AppearanceManifestError.MalformedJson, ex.Error);
        Assert.Equal(sourcePath, ex.Path);
        Assert.Contains(sourcePath, ex.Message);
    }

    [Fact]
    public void Load_ReadsSidecarBesideMapManifest()
    {
        string root = CreateTempRoot();
        try
        {
            string assetRoot = Path.Combine(root, "assets", "sprites");
            Directory.CreateDirectory(assetRoot);
            File.WriteAllText(Path.Combine(assetRoot, "manifest.json"), "map manifest");
            File.WriteAllText(Path.Combine(assetRoot, "appearance-manifest.json"), ConverterContractJson);

            AppearanceManifest manifest = AppearanceManifest.Load(assetRoot);

            Assert.True(manifest.TryGetReference(AppearancePartKind.Body, 1, 3, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingSidecarReportsManifestNotFoundWithFullPath()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(root);

            AppearanceManifestException ex = Assert.Throws<AppearanceManifestException>(() => AppearanceManifest.Load(root));

            Assert.Equal(AppearanceManifestError.ManifestNotFound, ex.Error);
            Assert.Equal(Path.Combine(root, "appearance-manifest.json"), ex.Path);
            Assert.Contains("appearance-manifest.json", ex.Message);
            Assert.Null(ex.InnerException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_DirectoryAtSidecarPathReportsReadFailed()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "appearance-manifest.json"));

            AppearanceManifestException ex = Assert.Throws<AppearanceManifestException>(() => AppearanceManifest.Load(root));

            Assert.Equal(AppearanceManifestError.ReadFailed, ex.Error);
            Assert.Equal(Path.Combine(root, "appearance-manifest.json"), ex.Path);
            Assert.NotNull(ex.InnerException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_MalformedSidecarReportsMalformedJsonWithFullPath()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "appearance-manifest.json"), "not json");

            AppearanceManifestException ex = Assert.Throws<AppearanceManifestException>(() => AppearanceManifest.Load(root));

            Assert.Equal(AppearanceManifestError.MalformedJson, ex.Error);
            Assert.Equal(Path.Combine(root, "appearance-manifest.json"), ex.Path);
            Assert.Contains("appearance-manifest.json", ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_NullOrBlankPathThrowsArgumentError()
    {
        Assert.Throws<ArgumentException>(() => AppearanceManifest.Load(null!));
        Assert.Throws<ArgumentException>(() => AppearanceManifest.Load("  "));
    }

    [Fact]
    public void AppearanceManifestException_IsIOExceptionWithTypedErrorAndPreservedInnerException()
    {
        string root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "appearance-manifest.json"));

            AppearanceManifestException ex = Assert.Throws<AppearanceManifestException>(() => AppearanceManifest.Load(root));

            Assert.IsAssignableFrom<IOException>(ex);
            Assert.Equal(AppearanceManifestError.ReadFailed, ex.Error);
            Assert.Equal(Path.Combine(root, "appearance-manifest.json"), ex.Path);
            Assert.NotNull(ex.InnerException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertParseError(string json, AppearanceManifestError error, string? messageFragment = null)
    {
        AppearanceManifestException ex = Assert.Throws<AppearanceManifestException>(() => AppearanceManifest.Parse(json));
        Assert.Equal(error, ex.Error);
        Assert.Equal("<memory>", ex.Path);
        if (messageFragment is not null)
        {
            Assert.Contains(messageFragment, ex.Message);
        }
    }

    private static string CreateTempRoot()
        => Path.Combine(Path.GetTempPath(), "appearance-manifest-tests-" + Guid.NewGuid().ToString("N"));
}
