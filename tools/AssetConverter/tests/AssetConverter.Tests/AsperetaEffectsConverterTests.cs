using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
using AssetConverter.Tests.Fixtures;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaEffectsConverterTests
{
    [Fact]
    public void RealDataset_FormerCharacterBuckets_AreEmittedAsEffects()
    {
        string outRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var catalog = AsperetaAnimationCatalog.Load(Paths.AsperetaData, Paths.AsperetaCompiledEnc);
            var result = AsperetaEffectsConverter.Convert(catalog, outRoot);

            Assert.Equal(0, result.Failed);
            Assert.Empty(result.Failures);

            foreach (int sourceId in new[] { 55228, 95170, 96230, 97090 })
            {
                string path = EffectPath(outRoot, sourceId);
                Assert.True(File.Exists(path), $"expected {path}");
            }
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true);
        }
    }

    [Fact]
    public void EveryUnclaimedDefinition_IsEmittedAtOffsetId()
    {
        var (root, data, enc) = CreateRoot();
        try
        {
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[]
                {
                    (55228, new[] { 1000, 1001 }, 0),
                    (95170, new[] { 1002, 1003 }, 0),
                    (96230, new[] { 1004, 1005 }, 0),
                    (97090, new[] { 1006, 1007 }, 0),
                    (123, new[] { 1008, 1009 }, 0),
                    (115123, new[] { 1010, 1011 }, 0),
                });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[]
                {
                    (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48), (1002, 48, 0, 24, 48),
                    (1003, 72, 0, 24, 48), (1004, 96, 0, 24, 48), (1005, 120, 0, 24, 48),
                    (1006, 0, 48, 24, 48), (1007, 24, 48, 24, 48), (1008, 48, 48, 24, 48),
                    (1009, 72, 48, 24, 48), (1010, 96, 48, 24, 48), (1011, 120, 48, 24, 48),
                },
                Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc);

            var catalog = AsperetaAnimationCatalog.Load(data, enc);
            string outRoot = Path.Combine(root, "out");
            var result = AsperetaEffectsConverter.Convert(catalog, outRoot);

            Assert.Equal(6, result.EffectsWritten);
            Assert.Equal(0, result.Failed);
            Assert.Empty(result.Failures);

            foreach (int sourceId in new[] { 55228, 95170, 96230, 97090, 123, 115123 })
            {
                string path = EffectPath(outRoot, sourceId);
                Assert.True(File.Exists(path), $"expected {path}");
                string tres = File.ReadAllText(path);
                Assert.Contains($"\"name\": &\"{AsperetaSheets.GraphicBase + sourceId}\"", tres);
                Assert.Contains("res://Assets/Sprites/sheets/20000.png", tres);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompiledClaimedDefinition_IsNotEmittedAsEffect()
    {
        var (root, data, enc) = CreateRoot();
        try
        {
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[]
                {
                    (95170, new[] { 1000, 1001 }, 0),
                    (55228, new[] { 1002, 1003 }, 0),
                });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48), (1002, 48, 0, 24, 48), (1003, 72, 0, 24, 48) },
                Array.Empty<(int, int[])>());
            var indexes = new int[32];
            indexes[0] = 95170;
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc, (AnimationType.Body, 200, indexes));

            var catalog = AsperetaAnimationCatalog.Load(data, enc);
            Assert.True(catalog.ClaimedIds.Contains(95170));

            string outRoot = Path.Combine(root, "out");
            var result = AsperetaEffectsConverter.Convert(catalog, outRoot);

            Assert.Equal(1, result.EffectsWritten);
            Assert.Equal(0, result.Failed);
            Assert.False(File.Exists(EffectPath(outRoot, 95170)));
            Assert.True(File.Exists(EffectPath(outRoot, 55228)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EffectRetainsSourceTiming()
    {
        var (root, data, enc) = CreateRoot();
        try
        {
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[]
                {
                    (115123, new[] { 1000, 1001 }, 10),
                    (123, new[] { 1002, 1003 }, 0),
                });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48), (1002, 48, 0, 24, 48), (1003, 72, 0, 24, 48) },
                Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc);

            var catalog = AsperetaAnimationCatalog.Load(data, enc);
            string outRoot = Path.Combine(root, "out");
            AsperetaEffectsConverter.Convert(catalog, outRoot);

            string spell = File.ReadAllText(EffectPath(outRoot, 115123));
            Assert.Contains("\"speed\": 20.0", spell);
            string emote = File.ReadAllText(EffectPath(outRoot, 123));
            Assert.Contains("\"speed\": 8.0", emote);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MixedSheetFrames_PreserveSourceOrderAndSheets()
    {
        var (root, data, enc) = CreateRoot();
        try
        {
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (55228, new[] { 1000, 2000, 1001 }, 0) });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1000, 0, 0, 16, 32), (1001, 0, 0, 32, 40) },
                Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(data, 2,
                new[] { (2000, 0, 0, 24, 48) },
                Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc);

            var catalog = AsperetaAnimationCatalog.Load(data, enc);
            string outRoot = Path.Combine(root, "out");
            AsperetaEffectsConverter.Convert(catalog, outRoot);

            string tres = File.ReadAllText(EffectPath(outRoot, 55228));
            Assert.Contains("res://Assets/Sprites/sheets/20000.png", tres);
            Assert.Contains("res://Assets/Sprites/sheets/20001.png", tres);
            Assert.True(tres.IndexOf("Rect2(0, 0, 16, 32)") < tres.IndexOf("Rect2(0, 0, 24, 48)"));
            Assert.True(tres.IndexOf("Rect2(0, 0, 24, 48)") < tres.IndexOf("Rect2(0, 0, 32, 40)"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnresolvedStandaloneDefinition_IsReportedWithoutFile()
    {
        var (root, data, enc) = CreateRoot();
        try
        {
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (96230, new[] { 424242, 424243 }, 0) });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1000, 0, 0, 24, 48) },
                Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc);

            var catalog = AsperetaAnimationCatalog.Load(data, enc);
            string outRoot = Path.Combine(root, "out");
            var result = AsperetaEffectsConverter.Convert(catalog, outRoot);

            Assert.Equal(0, result.EffectsWritten);
            Assert.Equal(0, result.Failed);
            Assert.False(File.Exists(EffectPath(outRoot, 96230)));
            string diagnostic = Assert.Single(result.Diagnostics);
            Assert.Contains("96230", diagnostic);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RepeatedConversion_IsDeterministic_WithExactMetadataKeys()
    {
        var (root, data, enc) = CreateRoot();
        try
        {
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[]
                {
                    (123, new[] { 1000, 1001 }, 0),
                    (55228, new[] { 1002, 1003 }, 0),
                });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[]
                {
                    (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48),
                    (1002, 48, 0, 24, 64), (1003, 72, 0, 24, 64),
                },
                Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc);

            var catalog = AsperetaAnimationCatalog.Load(data, enc);
            string first = Path.Combine(root, "out1");
            string second = Path.Combine(root, "out2");

            string resourcesDir = Path.Combine(first, "Assets", "Resources");
            Directory.CreateDirectory(resourcesDir);
            File.WriteAllText(Path.Combine(resourcesDir, "AnimationHeights.txt"), "Body-10001,80\n");

            var firstResult = AsperetaEffectsConverter.Convert(catalog, first);
            var secondResult = AsperetaEffectsConverter.Convert(catalog, second);

            Assert.Equal(2, firstResult.EffectsWritten);
            Assert.Equal(2, secondResult.EffectsWritten);
            Assert.Equal(0, firstResult.Failed);
            Assert.Equal(0, secondResult.Failed);

            Assert.Equal(File.ReadAllBytes(EffectPath(first, 123)), File.ReadAllBytes(EffectPath(second, 123)));
            Assert.Equal(File.ReadAllBytes(EffectPath(first, 55228)), File.ReadAllBytes(EffectPath(second, 55228)));

            string mergedHeights = File.ReadAllText(Path.Combine(first, "Assets", "Resources", "AnimationHeights.txt"));
            Assert.Equal("700123,48\nBody-10001,80\n", mergedHeights);
            string freshHeights = File.ReadAllText(Path.Combine(second, "Assets", "Resources", "AnimationHeights.txt"));
            Assert.Equal("700123,48\n", freshHeights);
            Assert.Equal("", File.ReadAllText(Path.Combine(first, "Assets", "Resources", "AnimationToFirstFrame.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (string Root, string Data, string Enc) CreateRoot()
    {
        string root = AnimationSourceFixture.CreateDirectory();
        return (root, AnimationSourceFixture.AsperetaDataDir(root), AnimationSourceFixture.AsperetaCompiledEncPath(root));
    }

    private static string EffectPath(string outRoot, int sourceId) =>
        Path.Combine(outRoot, "Assets", "Sprites", "Effects",
            (AsperetaSheets.GraphicBase + sourceId).ToString(), "animations.tres");
}
