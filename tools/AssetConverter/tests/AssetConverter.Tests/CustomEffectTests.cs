using Goose2.AssetConverter.Manifest;
using Xunit;

namespace AssetConverter.Tests;

public class CustomEffectTests
{
    [Fact]
    public void BossTarget_UsesFirstCustomEffectId()
    {
        Assert.Equal(900001, CustomEffect.BossTarget.Id);
        Assert.Equal(30022, CustomEffect.BossTarget.Sheet.SheetNumber);
        Assert.Equal(CustomEffect.All.Count, CustomEffect.All.Select(e => e.Id).Distinct().Count());
        Assert.All(CustomEffect.All, e => Assert.True(e.Id > CustomEffect.IdBase));
    }

    [Fact]
    public void BuildAnimation_PlaysEverySheetFrameInOrderAtEffectFps()
    {
        var anim = CustomEffect.BossTarget.BuildAnimation();

        Assert.Equal("900001", anim.Name);
        Assert.Equal(12f, anim.Speed);
        Assert.Equal(Enumerable.Range(1, 12), anim.Frames.Select(f => f.Frame.Index));
        Assert.Equal(Enumerable.Range(0, 12).Select(i => i * 64), anim.Frames.Select(f => f.Frame.X));
        Assert.All(anim.Frames, f =>
        {
            Assert.Equal("res://Assets/Sprites/sheets/30022.png", f.TexturePath);
            Assert.Equal((0, 64, 64), (f.Frame.Y, f.Frame.W, f.Frame.H));
        });
    }

    [Fact]
    public void Shield_PlaysSixteenTallFramesSoTheBubbleCoversTheCharacter()
    {
        var anim = CustomEffect.Shield.BuildAnimation();

        Assert.Equal(900002, CustomEffect.Shield.Id);
        Assert.Equal("900002", anim.Name);
        Assert.Equal(12f, anim.Speed);
        Assert.Equal(Enumerable.Range(1, 16), anim.Frames.Select(f => f.Frame.Index));
        Assert.All(anim.Frames, f =>
        {
            Assert.Equal("res://Assets/Sprites/sheets/30023.png", f.TexturePath);
            Assert.Equal((0, 64, 80), (f.Frame.Y, f.Frame.W, f.Frame.H));
        });
    }

    [Fact]
    public void WriteAll_WritesEffectSpriteFrames()
    {
        string root = Path.Combine(Path.GetTempPath(), $"custom-effect-{Guid.NewGuid():N}");

        try
        {
            Assert.Equal(2, CustomEffect.WriteAll(root));

            string tres = File.ReadAllText(Path.Combine(root, "Assets", "Sprites", "Effects", "900001", "animations.tres"));
            Assert.Contains("path=\"res://Assets/Sprites/sheets/30022.png\"", tres);
            Assert.Contains("region = Rect2(704, 0, 64, 64)", tres);
            Assert.Contains("\"name\": &\"900001\"", tres);

            string shield = File.ReadAllText(Path.Combine(root, "Assets", "Sprites", "Effects", "900002", "animations.tres"));
            Assert.Contains("region = Rect2(960, 0, 64, 80)", shield);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
