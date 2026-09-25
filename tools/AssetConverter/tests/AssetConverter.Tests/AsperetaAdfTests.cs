using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using AssetConverter.Tests.Fixtures;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaAdfTests
{
    private static string Adf(int n) => Path.Combine(Paths.AsperetaData, $"{n}.adf");

    [Fact]
    public void Animation_EncodedInterval_SurvivesParsing()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        var dataDir = AnimationSourceFixture.AsperetaDataDir(root);
        AnimationSourceFixture.WriteAsperetaAdf(dataDir, 1,
            new[] { (1200, 0, 0, 24, 48), (1201, 24, 0, 24, 48) },
            new[] { (500, new[] { 1200, 1201 }, 3), (501, new[] { 1201, 1200 }, 5) });

        var adf = AsperetaAdf.Load(Path.Combine(dataDir, "1.adf"));

        Assert.Equal(3, adf.Animations[500].Interval);
        Assert.Equal(5, adf.Animations[501].Interval);
    }

    [Fact]
    public void Animation_DefaultFixtureInterval_IsZero()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        var dataDir = AnimationSourceFixture.AsperetaDataDir(root);
        AnimationSourceFixture.WriteAsperetaAdf(dataDir, 1,
            new[] { (1200, 0, 0, 24, 48), (1201, 24, 0, 24, 48) },
            new[] { (500, new[] { 1200, 1201 }) });

        var adf = AsperetaAdf.Load(Path.Combine(dataDir, "1.adf"));

        Assert.Equal(0, adf.Animations[500].Interval);
    }

    [Fact]
    public void IllutiaAnimation_HasNoSourceInterval()
    {
        Assert.Null(new Animation(1).Interval);
    }

    [Fact]
    public void Sheet1_Bodies_HasExpectedFrames()
    {
        var adf = AsperetaAdf.Load(Adf(1));

        Assert.Equal(1, adf.FileNumber);
        Assert.Equal(AdfType.Graphic, adf.Type);
        Assert.Equal(12, adf.Frames.Count);
        Assert.Equal(1200, adf.FirstFrameIndex);

        var f0 = adf.Frames[0];
        Assert.Equal((1200, 0, 0, 24, 48), (f0.Index, f0.X, f0.Y, f0.W, f0.H));
    }

    [Fact]
    public void Sheet1_PayloadIsBmp()
    {
        var adf = AsperetaAdf.Load(Adf(1));
        Assert.Equal(41527, adf.FileData.Length);
        Assert.Equal((byte)'B', adf.FileData[0]);
        Assert.Equal((byte)'M', adf.FileData[1]);
    }

    [Fact]
    public void AllGraphicSheets_DecodeWithoutError()
    {
        int graphics = 0;
        foreach (var file in Directory.EnumerateFiles(Paths.AsperetaData, "*.adf"))
        {
            var adf = AsperetaAdf.Load(file);          // must not throw on any file
            if (adf.Type == AdfType.Graphic && adf.Frames.Count > 0) graphics++;
        }
        // 487 total .adf files in the dataset; 25 are sounds (unknown trailer == 36,
        // RIFF/WAV payload) reclassified to AdfType.Sound, leaving 462 graphic sheets.
        Assert.Equal(462, graphics);
    }
}
