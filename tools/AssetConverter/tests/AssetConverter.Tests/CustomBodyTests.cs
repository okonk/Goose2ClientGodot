using System.Text.Json;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Manifest;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

namespace AssetConverter.Tests;

public class CustomBodyTests
{
    [Fact]
    public void RuneTable_UsesFirstCustomBodyId()
    {
        Assert.Equal(20000, CustomBody.RuneTable.Id);
        Assert.Equal(30016, CustomBody.RuneTable.Sheet.SheetNumber);
        Assert.Equal(CustomBody.All.Count, CustomBody.All.Select(b => b.Id).Distinct().Count());
        Assert.All(CustomBody.All, b => Assert.True(b.Id >= CustomBody.IdBase));
    }

    [Fact]
    public void BuildResource_LoopsEveryFrameInEveryIdleAndWalkClip()
    {
        var resource = CustomBody.RuneTable.BuildResource();

        Assert.Equal(AnimationType.Body, resource.Type);
        Assert.Equal("Assets/Sprites/Bodies/20000/animations.tres", resource.RelativeOutputPath);
        foreach (string clip in new[] { "idle", "idle-no-equip", "idle-equip", "walk", "walk-no-equip", "walk-equip" })
            foreach (string dir in new[] { "down", "up", "left", "right" })
            {
                var anim = Assert.Single(resource.Animations, a => a.Name == $"{clip}-{dir}");
                Assert.True(anim.Loop);
                Assert.Equal(Enumerable.Range(1, 8), anim.Frames.Select(f => f.Frame.Index));
                Assert.Equal(Enumerable.Range(0, 8).Select(i => i * 64), anim.Frames.Select(f => f.Frame.X));
                Assert.All(anim.Frames, f =>
                {
                    Assert.Equal(30016, f.SheetNumber);
                    Assert.Equal("res://Assets/Sprites/sheets/30016.png", f.TexturePath);
                    Assert.Equal((0, 64, 48), (f.Frame.Y, f.Frame.W, f.Frame.H));
                });
                Assert.Equal(48, resource.AnimationHeights[$"Body-20000-{clip}-{dir}"]);
            }
        Assert.Equal(new AnimationFrameInfo(30016, 1, 64, 48), resource.AnimationToFirstFrame["Body-20000"]);
    }

    [Fact]
    public void AppearanceManifest_ReferencesCustomSheetGraphicWithoutAsperetaOffset()
    {
        using var json = JsonDocument.Parse(AppearanceManifestBuilder.Build(CustomBody.BuildResources()));
        var body = json.RootElement.GetProperty("parts").GetProperty("Body").GetProperty("20000");

        foreach (string variant in new[] { "noEquip", "equip", "noEquipUp", "equipUp", "noEquipRight", "equipRight", "noEquipLeft", "equipLeft" })
            Assert.Equal(new[] { 30016, 1 }, body.GetProperty(variant).EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public void WriteAll_WritesSpriteFramesAndOverridesStaleMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), $"custom-body-{Guid.NewGuid():N}");
        string resources = Path.Combine(root, "Assets", "Resources");
        Directory.CreateDirectory(resources);

        try
        {
            File.WriteAllText(Path.Combine(resources, "AnimationHeights.txt"), "Body-1-idle-down,72\nBody-20000-idle-down,64\n");
            File.WriteAllText(Path.Combine(resources, "AnimationToFirstFrame.txt"), "Body-20000,30016,1,32,32\n");

            Assert.Equal(1, CustomBody.WriteAll(root));

            string tres = File.ReadAllText(Path.Combine(root, "Assets", "Sprites", "Bodies", "20000", "animations.tres"));
            Assert.Contains("path=\"res://Assets/Sprites/sheets/30016.png\"", tres);
            Assert.Contains("region = Rect2(448, 0, 64, 48)", tres);
            Assert.Contains("\"name\": &\"idle-down\"", tres);

            string[] heights = File.ReadAllLines(Path.Combine(resources, "AnimationHeights.txt"));
            Assert.Contains("Body-1-idle-down,72", heights);
            Assert.Contains("Body-20000-idle-down,48", heights);
            Assert.DoesNotContain("Body-20000-idle-down,64", heights);
            Assert.Contains("Body-20000,30016,1,64,48", File.ReadAllLines(Path.Combine(resources, "AnimationToFirstFrame.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AnimationManifest_TagsBodySheetWithBodyCategory()
    {
        var sheets = new SortedDictionary<int, AnimationManifestSheet>();
        var categories = new Dictionary<int, List<(string Name, int? Id)>>();

        CustomBody.AddManifestSheets(sheets, categories);

        Assert.True(sheets.ContainsKey(30016));
        Assert.Equal(new[] { ("Body", (int?)20000) }, categories[30016]);
        Assert.Throws<InvalidDataException>(() => CustomBody.AddManifestSheets(sheets, categories));
    }
}
