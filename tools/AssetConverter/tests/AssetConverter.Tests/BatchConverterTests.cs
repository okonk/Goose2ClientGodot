using Goose2.AssetConverter;
using Xunit;

namespace AssetConverter.Tests;

public class BatchConverterTests
{
    [Fact]
    public void Convert_SoundFile_ReportedAsFailure()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "ac_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            // File 433 is a Sound .adf (type=2), not a graphic.
            var result = BatchConverter.Convert(Paths.IllutiaData, outDir, onlyFileNumbers: new[] { 433 });

            Assert.Equal(0, result.Succeeded);
            Assert.Equal(1, result.Failed);
            Assert.Single(result.Failures);
            Assert.Contains("not a graphic", result.Failures[0]);
            Assert.DoesNotContain("433.png", Directory.GetFiles(outDir));
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Convert_WritesPngForGraphicSheet_AndReportsResults()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "ac_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = BatchConverter.Convert(Paths.IllutiaData, outDir, onlyFileNumbers: new[] { 1000 });

            Assert.Equal(1, result.Succeeded);
            Assert.Equal(0, result.Failed);
            Assert.True(File.Exists(Path.Combine(outDir, "1000.png")));
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }
}
