using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
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

    [Fact]
    public void Convert_BmpPayload_4590_Succeeds()
    {
        // 4590.adf contains a BMP payload (starts with "BM"), not GIF.
        // The batch converter must dispatch to ImageSharp for BMP payloads.
        var outDir = Path.Combine(Path.GetTempPath(), "ac_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = BatchConverter.Convert(Paths.IllutiaData, outDir, onlyFileNumbers: new[] { 4590 });

            Assert.Equal(1, result.Succeeded);
            Assert.Equal(0, result.Failed);
            var pngPath = Path.Combine(outDir, "4590.png");
            Assert.True(File.Exists(pngPath), "4590.png was not written");
            Assert.True(new FileInfo(pngPath).Length > 0, "4590.png is empty");
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Convert_UnsupportedPayload_4589_ReportedAsFailure()
    {
        // 4589.adf is a graphic ADF whose decoded payload starts with 0xCD 0xCF 0xCC
        // — not GIF, not BMP, not any format ImageSharp can decode.
        // It should fail with a clear unsupported-format message.
        var outDir = Path.Combine(Path.GetTempPath(), "ac_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = BatchConverter.Convert(Paths.IllutiaData, outDir, onlyFileNumbers: new[] { 4589 });

            Assert.Equal(0, result.Succeeded);
            Assert.Equal(1, result.Failed);
            Assert.Single(result.Failures);
            Assert.Contains("Unsupported", result.Failures[0]);
            Assert.DoesNotContain("4589.png", Directory.GetFiles(outDir));
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Convert_GifPayload_StillUsesGifLoader()
    {
        // Verify that GIF payloads (e.g. file 1000) still go through the custom GifLoader
        // and produce output.
        var adf = new AdfFile(Paths.Adf(1000));
        Assert.Equal(AdfType.Graphic, adf.Type);
        Assert.Equal((byte)'G', adf.FileData[0]);
        Assert.Equal((byte)'I', adf.FileData[1]);
        Assert.Equal((byte)'F', adf.FileData[2]);

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
