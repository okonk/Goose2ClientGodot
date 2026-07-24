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

            // Every emitted directory is offset by GraphicBase (700000+)
            var dirs = Directory.GetDirectories(Path.Combine(outRoot, "Assets", "Sprites", "Effects"));
            Assert.All(dirs, d => Assert.True(
                int.Parse(Path.GetFileName(d)) >= AsperetaSheets.GraphicBase));

            // Spot-check: each animations.tres references an injected sheet png (20000+)
            string sample = File.ReadAllText(Path.Combine(dirs[0], "animations.tres"));
            Assert.Contains("res://Assets/Sprites/sheets/2", sample); // 20000+ png
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true);
        }
    }
}
