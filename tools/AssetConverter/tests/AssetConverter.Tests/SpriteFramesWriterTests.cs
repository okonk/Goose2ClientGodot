using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

public class SpriteFramesWriterTests
{
    [Fact]
    public void Build_EmitsAtlasRegionsAndAnimationFromTopDownFrames()
    {
        var adf = new AdfFile(Goose2.AssetConverter.Paths.Adf(1000));

        string tres = SpriteFramesWriter.Build(adf, texturePath: "res://Assets/Sprites/sheets/1000.png");

        Assert.Contains("[gd_resource type=\"SpriteFrames\" format=3", tres);
        Assert.Contains("path=\"res://Assets/Sprites/sheets/1000.png\"", tres);
        // First frame is top-left 48x64 (Frames[0] = index 108760 @ 0,0).
        Assert.Contains("region = Rect2(0, 0, 48, 64)", tres);
        // Last frame is at (48, 192).
        Assert.Contains("region = Rect2(48, 192, 48, 64)", tres);
        Assert.Contains("\"name\": &\"all\"", tres);
        Assert.Contains("\"loop\": true", tres);
        // 8 frames → 8 AtlasTexture sub-resources.
        Assert.Equal(8, System.Text.RegularExpressions.Regex.Matches(tres, "type=\"AtlasTexture\"").Count);
    }
}
