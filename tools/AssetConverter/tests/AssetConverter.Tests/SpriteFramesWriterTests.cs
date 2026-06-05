using System;
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

    [Fact]
    public void BuildCompiled_EmitsMultipleAnimationsAcrossMultipleSheets()
    {
        var adf115 = new AdfFile(Goose2.AssetConverter.Paths.Adf(115));
        var adf116 = new AdfFile(Goose2.AssetConverter.Paths.Adf(116));

        var animations = new[]
        {
            SpriteFramesAnimationSpec.FromFrames(
                "walk-left", 115, "res://Assets/Sprites/sheets/115.png", adf115.Animations![3220].Frames),
            SpriteFramesAnimationSpec.FromFrames(
                "walk-down", 115, "res://Assets/Sprites/sheets/115.png", adf115.Animations![3221].Frames),
            SpriteFramesAnimationSpec.FromFrames(
                "walk-equip-left", 116, "res://Assets/Sprites/sheets/116.png", adf116.Animations![3244].Frames),
        };

        string tres = SpriteFramesWriter.Build(animations);

        Assert.Contains("path=\"res://Assets/Sprites/sheets/115.png\"", tres);
        Assert.Contains("path=\"res://Assets/Sprites/sheets/116.png\"", tres);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(tres, "ext_resource type=\"Texture2D\"").Count);
        // ext_resource ids must match ExtResource references
        Assert.Contains("id=\"Tex_0\"", tres);
        Assert.Contains("id=\"Tex_1\"", tres);
        Assert.Contains("ExtResource(\"Tex_0\")", tres);
        Assert.Contains("ExtResource(\"Tex_1\")", tres);
        Assert.Contains("region = Rect2(0, 0, 24, 48)", tres);
        Assert.Contains("region = Rect2(0, 48, 24, 48)", tres);
        Assert.Contains("\"name\": &\"walk-left\"", tres);
        Assert.Contains("\"name\": &\"walk-down\"", tres);
        Assert.Contains("\"name\": &\"walk-equip-left\"", tres);

        // Assert valid multi-animation array syntax: no malformed double-open braces
        Assert.DoesNotContain("animations = [{\n\n{", tres);
        Assert.DoesNotContain("animations = [{\n{", tres);
        // Must have exactly 3 animation name entries (one per animation object)
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(tres, "\"name\": &\"").Count);
        // The resource section should end with "}]" (valid array close)
        Assert.EndsWith("}]\n", tres);
    }

    [Fact]
    public void BuildCompiled_ThrowsForEmptyAnimationList()
    {
        Assert.Throws<ArgumentException>(() => SpriteFramesWriter.Build(Array.Empty<SpriteFramesAnimationSpec>()));
    }
}
