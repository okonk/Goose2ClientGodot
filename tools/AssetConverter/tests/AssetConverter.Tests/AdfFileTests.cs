using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Xunit;

namespace AssetConverter.Tests;

public class AdfFileTests
{
    [Fact]
    public void Sheet1000_HasExpectedFramesMatchingUnityMeta()
    {
        var adf = new AdfFile(Paths.Adf(1000));

        Assert.Equal(1000, adf.FileNumber);
        Assert.Equal(AdfType.Graphic, adf.Type);
        Assert.Equal(8, adf.FrameCount);
        Assert.Equal(108760, adf.FirstFrameIndex);
        Assert.Equal(8, adf.Frames.Count);

        var f0 = adf.Frames[0];
        Assert.Equal((108760, 0, 0, 48, 64), (f0.Index, f0.X, f0.Y, f0.W, f0.H));

        var f2 = adf.Frames[2];
        Assert.Equal((108762, 0, 64, 48, 64), (f2.Index, f2.X, f2.Y, f2.W, f2.H));

        var f7 = adf.Frames[7];
        Assert.Equal((108767, 48, 192, 48, 64), (f7.Index, f7.X, f7.Y, f7.W, f7.H));
    }

    [Fact]
    public void DecodedFileData_LooksLikeAGif()
    {
        var adf = new AdfFile(Paths.Adf(1000));
        // After the per-byte de-offset + 790-byte de-interleave, the payload is a GIF.
        Assert.True(adf.FileData.Length > 6);
        Assert.Equal((byte)'G', adf.FileData[0]);
        Assert.Equal((byte)'I', adf.FileData[1]);
        Assert.Equal((byte)'F', adf.FileData[2]);
    }
}
