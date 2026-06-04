using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Gif;
using Xunit;

namespace AssetConverter.Tests;

[Collection("AdfFileCollection")]
public class GifLoaderTests
{
    [Fact]
    public void Sheet1000_DecodesTo96x256Rgba()
    {
        var adf = new AdfFile(Paths.Adf(1000));

        var rgba = GifLoader.Load(adf.FileData, out int width, out int height);

        Assert.Equal(96, width);
        Assert.Equal(256, height);
        Assert.Equal(96 * 256 * 4, rgba.Length);
    }
}
