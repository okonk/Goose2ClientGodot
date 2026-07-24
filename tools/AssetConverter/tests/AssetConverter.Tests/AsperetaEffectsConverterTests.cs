using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaEffectsConverterTests
{
    [Fact]
    public void WritesOffsetEffectResources_SkippingCompiledAnimations()
    {
        string outRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var result = AsperetaEffectsConverter.Convert(
                Paths.AsperetaData, Paths.AsperetaCompiledEnc, outRoot);

            Assert.True(result.EffectsWritten > 0);
            Assert.Equal(0, result.Failed);
            Assert.Empty(result.Failures);

            // Every emitted directory is offset by GraphicBase (700000+)
            var dirs = Directory.GetDirectories(Path.Combine(outRoot, "Assets", "Sprites", "Effects"));
            Assert.All(dirs, d => Assert.True(
                int.Parse(Path.GetFileName(d)) >= AsperetaSheets.GraphicBase));

            // Spot-check: each animations.tres references an injected sheet png (20000+)
            // and names the clip after the offset effect id.
            string effectId = Path.GetFileName(dirs[0]);
            string sample = File.ReadAllText(Path.Combine(dirs[0], "animations.tres"));
            Assert.Contains("res://Assets/Sprites/sheets/2", sample); // 20000+ png
            Assert.Contains($"\"name\": &\"{effectId}\"", sample);

            // A non-zero compiled animation index must not appear under Effects/
            int compiledId = AsperetaCompiledEnc.Load(Paths.AsperetaCompiledEnc)
                .SelectMany(a => a.Indexes)
                .First(id => id != 0);
            string compiledEffectPath = Path.Combine(
                outRoot,
                "Assets", "Sprites", "Effects",
                (AsperetaSheets.GraphicBase + compiledId).ToString(),
                "animations.tres");
            Assert.False(File.Exists(compiledEffectPath));
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true);
        }
    }
}
