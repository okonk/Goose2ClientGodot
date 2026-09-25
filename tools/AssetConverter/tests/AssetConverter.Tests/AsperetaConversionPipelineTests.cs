using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
using Goose2.AssetConverter.Manifest;
using Goose2.AssetConverter.SpriteFrames;
using AssetConverter.Tests.Fixtures;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaConversionPipelineTests
{
    [Fact]
    public void HermeticRoot_WritesResourcesEffectsMetadataAndDiagnostics()
    {
        var (root, data, enc) = WriteFixture();
        try
        {
            string outRoot = Path.Combine(root, "out");
            var result = AsperetaConversionPipeline.Convert(data, enc, outRoot);

            Assert.Equal(2, result.Resources.Count);
            Assert.Equal(2, result.ResourcesWritten);
            Assert.Equal(2, result.EffectsWritten);
            Assert.Equal(0, result.Failed);
            Assert.Empty(result.Failures);

            string body = Path.Combine(outRoot, "Assets", "Sprites", "Bodies", "10010", "animations.tres");
            string hair = Path.Combine(outRoot, "Assets", "Sprites", "Hair", "10011", "animations.tres");
            Assert.True(File.Exists(body), $"missing {body}");
            Assert.True(File.Exists(hair), $"missing {hair}");
            string bodyTres = File.ReadAllText(body);
            Assert.Contains("walk-no-equip-down", bodyTres);
            Assert.Contains("\"speed\": 8.0", bodyTres);

            Assert.True(File.Exists(EffectPath(outRoot, 55228)), "missing effect 55228");
            Assert.True(File.Exists(EffectPath(outRoot, 95170)), "missing effect 95170");
            Assert.False(File.Exists(EffectPath(outRoot, 96230)), "unresolved effect 96230 must not be written");

            string heights = File.ReadAllText(Path.Combine(outRoot, "Assets", "Resources", "AnimationHeights.txt"));
            Assert.Contains("755228,48", heights);
            Assert.Contains("795170,48", heights);
            string firstFrames = File.ReadAllText(Path.Combine(outRoot, "Assets", "Resources", "AnimationToFirstFrame.txt"));
            Assert.Equal("Body-10010,20000,1004,24,48\n", firstFrames);

            Assert.Equal(2, result.Diagnostics.Count);
            Assert.Contains(result.Diagnostics, d => d.Contains("424242"));
            Assert.Contains(result.Diagnostics, d => d.Contains("96230"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RepeatedConversion_IsDeterministic()
    {
        var (root, data, enc) = WriteFixture();
        try
        {
            string out1 = Path.Combine(root, "out1");
            string out2 = Path.Combine(root, "out2");
            var r1 = AsperetaConversionPipeline.Convert(data, enc, out1);
            var r2 = AsperetaConversionPipeline.Convert(data, enc, out2);

            Assert.Equal(r1.ResourcesWritten, r2.ResourcesWritten);
            Assert.Equal(r1.EffectsWritten, r2.EffectsWritten);
            Assert.Equal(r1.Diagnostics, r2.Diagnostics);

            var files1 = Directory.EnumerateFiles(out1, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(out1, p)).OrderBy(p => p).ToList();
            var files2 = Directory.EnumerateFiles(out2, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(out2, p)).OrderBy(p => p).ToList();
            Assert.Equal(files1, files2);
            foreach (var rel in files1)
                Assert.True(File.ReadAllBytes(Path.Combine(out1, rel)).SequenceEqual(File.ReadAllBytes(Path.Combine(out2, rel))), rel);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExistingCombinedMetadata_IsPreservedBySafeMerge()
    {
        var (root, data, enc) = WriteFixture();
        try
        {
            string outRoot = Path.Combine(root, "out");
            string resourcesDir = Path.Combine(outRoot, "Assets", "Resources");
            Directory.CreateDirectory(resourcesDir);
            File.WriteAllText(Path.Combine(resourcesDir, "AnimationHeights.txt"), "Illutia-Key,80\n");
            File.WriteAllText(Path.Combine(resourcesDir, "AnimationToFirstFrame.txt"), "Illutia-Body,20000,1,24,48\n");

            var result = AsperetaConversionPipeline.Convert(data, enc, outRoot);
            Assert.Equal(0, result.Failed);

            string heights = File.ReadAllText(Path.Combine(resourcesDir, "AnimationHeights.txt"));
            Assert.Contains("Illutia-Key,80", heights);
            Assert.Contains("755228,48", heights);
            string firstFrames = File.ReadAllText(Path.Combine(resourcesDir, "AnimationToFirstFrame.txt"));
            Assert.Contains("Illutia-Body,20000,1,24,48", firstFrames);
            Assert.Contains("Body-10010,20000,1004,24,48", firstFrames);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EntryWithNoResolvableSlots_IsOmittedWithoutEmptyResource()
    {
        var (root, data, enc) = CreateRoot();
        try
        {
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                Array.Empty<(int, int[], int)>());
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1000, 0, 0, 24, 48) },
                Array.Empty<(int, int[], int)>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc,
                (AnimationType.Body, 10, new[] { 424242, 424242 }));

            string outRoot = Path.Combine(root, "out");
            var result = AsperetaConversionPipeline.Convert(data, enc, outRoot);

            Assert.Equal(0, result.ResourcesWritten);
            Assert.Equal(0, result.Failed);
            Assert.Empty(result.Failures);
            var spritesDir = Path.Combine(outRoot, "Assets", "Sprites");
            var tresFiles = Directory.Exists(spritesDir)
                ? Directory.EnumerateFiles(spritesDir, "animations.tres", SearchOption.AllDirectories).ToList()
                : new List<string>();
            Assert.Empty(tresFiles);
            var diagnostics = result.Diagnostics.ToList();
            Assert.Contains(diagnostics, d => d.Contains("424242"));
            Assert.Contains(diagnostics, d => d.Contains("no resolvable clips"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AllAndAsperetaOrchestrations_ProduceIdenticalAsperetaOutputs()
    {
        string allRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string asperetaRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            AnimationBatchConverter.Convert(Paths.IllutiaData, Paths.CompiledEnc, allRoot, includeEffects: true);
            var allAsp = AsperetaConversionPipeline.Convert(Paths.AsperetaData, Paths.AsperetaCompiledEnc, allRoot);
            WriteAsperetaManifests(allRoot, allAsp);

            var asperetaAsp = AsperetaConversionPipeline.Convert(Paths.AsperetaData, Paths.AsperetaCompiledEnc, asperetaRoot);
            WriteAsperetaManifests(asperetaRoot, asperetaAsp);

            Assert.Equal(0, allAsp.Failed);
            Assert.Equal(0, asperetaAsp.Failed);
            Assert.Equal(allAsp.ResourcesWritten, asperetaAsp.ResourcesWritten);
            Assert.Equal(allAsp.EffectsWritten, asperetaAsp.EffectsWritten);
            Assert.Equal(allAsp.Diagnostics, asperetaAsp.Diagnostics);

            string spritesA = Path.Combine(allRoot, "Assets", "Sprites");
            string spritesB = Path.Combine(asperetaRoot, "Assets", "Sprites");
            var asperetaFiles = Directory.EnumerateFiles(spritesA, "animations.tres", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(spritesA, p))
                .Where(AsperetaOutputPath)
                .OrderBy(p => p)
                .ToList();
            Assert.NotEmpty(asperetaFiles);
            foreach (var rel in asperetaFiles)
            {
                string a = Path.Combine(spritesA, rel);
                string b = Path.Combine(spritesB, rel);
                Assert.True(File.Exists(b), $"aspereta output missing under aspereta root: {rel}");
                Assert.True(File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)), rel);
            }

            foreach (var name in new[] { "manifest.json", "animation-manifest.json", "appearance-manifest.json" })
            {
                Assert.True(
                    File.ReadAllBytes(Path.Combine(spritesA, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(spritesB, name))),
                    name);
            }
        }
        finally
        {
            if (Directory.Exists(allRoot)) Directory.Delete(allRoot, recursive: true);
            if (Directory.Exists(asperetaRoot)) Directory.Delete(asperetaRoot, recursive: true);
        }
    }

    private static void WriteAsperetaManifests(string root, AsperetaConversionResult asp)
    {
        AppearanceManifestFileStore.Write(root,
            AsperetaConversionPipeline.BuildCombinedAppearanceManifest(
                Paths.IllutiaData, Paths.CompiledEnc, asp.Resources));
        ManifestFileStore.WriteCombined(root,
            () => FrameManifestBuilder.BuildCombined(Paths.IllutiaData, asp.Catalog),
            () => AnimationManifestBuilder.BuildCombined(
                Paths.IllutiaData, Paths.CompiledEnc, asp.Catalog, Paths.ItemTileSheets));
    }

    private static bool AsperetaOutputPath(string relativeTresPath)
    {
        var parts = relativeTresPath.Split(Path.DirectorySeparatorChar);
        int id = int.Parse(parts[^2]);
        return parts[^3] == "Effects"
            ? id >= AsperetaSheets.GraphicBase
            : id >= AsperetaSheets.BodyBase;
    }

    private static (string Root, string Data, string Enc) WriteFixture()
    {
        var (root, data, enc) = CreateRoot();
        AnimationSourceFixture.WriteAsperetaAdf(data, 0,
            Array.Empty<(int, int, int, int, int)>(),
            new[]
            {
                (9001, new[] { 1000, 1001 }, 0),
                (9002, new[] { 1002, 1003 }, 0),
                (9003, new[] { 1004, 1005 }, 0),
                (9004, new[] { 1006, 1007 }, 0),
                (55228, new[] { 1008, 1009 }, 0),
                (95170, new[] { 1010, 1011 }, 0),
                (96230, new[] { 424243, 424244 }, 0),
            });
        AnimationSourceFixture.WriteAsperetaAdf(data, 1,
            new[]
            {
                (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48), (1002, 48, 0, 24, 48),
                (1003, 72, 0, 24, 48), (1004, 0, 48, 24, 48), (1005, 24, 48, 24, 48),
                (1006, 48, 48, 24, 48), (1007, 72, 48, 24, 48), (1008, 0, 96, 24, 48),
                (1009, 24, 96, 24, 48), (1010, 48, 96, 24, 48), (1011, 72, 96, 24, 48),
            },
            Array.Empty<(int, int[], int)>());
        AnimationSourceFixture.WriteAsperetaCompiledEnc(enc,
            (AnimationType.Body, 10, new[] { 9001, 0, 0, 0, 9002, 0, 0, 0, 9003, 0, 0, 0, 9004 }),
            (AnimationType.Hair, 11, new[] { 9001, 0, 0, 0, 424242 }));
        return (root, data, enc);
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
