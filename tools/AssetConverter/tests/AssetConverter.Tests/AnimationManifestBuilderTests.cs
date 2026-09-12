using System.Text.Json;
using AssetConverter.Tests.Fixtures;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Manifest;

namespace AssetConverter.Tests;

public class AnimationManifestBuilderTests
{
    [Fact]
    public void Build_EmitsVersionOneAndFpsEight()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 115, 3205, 4,
                (3205, new[] { 3205, 3206 }));
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            using var doc = JsonDocument.Parse(Build(root));
            Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
            var animations = doc.RootElement.GetProperty("animations");
            Assert.Equal(1, animations.GetArrayLength());
            Assert.Equal(8, animations[0].GetProperty("fps").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_MapsAllEightEquipmentCategoryNames()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var types = new[]
            {
                AnimationType.Body, AnimationType.Hair, AnimationType.Eyes, AnimationType.Chest,
                AnimationType.Helm, AnimationType.Legs, AnimationType.Feet, AnimationType.Hand,
            };
            for (var i = 0; i < types.Length; i++)
                AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 100 + i, 3200, 1);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                types.Select((type, i) => (type, 7 + i, new[] { 100 + i })).ToArray());

            using var doc = JsonDocument.Parse(Build(root));
            var sheets = doc.RootElement.GetProperty("sheets");
            for (var i = 0; i < types.Length; i++)
            {
                var categories = sheets.GetProperty((100 + i).ToString()).GetProperty("categories");
                Assert.Equal(1, categories.GetArrayLength());
                Assert.Equal(types[i].ToString(), categories[0].GetProperty("name").GetString());
                Assert.Equal(7 + i, categories[0].GetProperty("id").GetInt32());
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_SharedSheetRetainsBothEquipmentMappings()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 115, 3205, 4);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }),
                (AnimationType.Hair, 2, new[] { 115 }));

            using var doc = JsonDocument.Parse(Build(root));
            var categories = doc.RootElement.GetProperty("sheets").GetProperty("115").GetProperty("categories");
            Assert.Equal(2, categories.GetArrayLength());
            Assert.Equal("Body", categories[0].GetProperty("name").GetString());
            Assert.Equal(1, categories[0].GetProperty("id").GetInt32());
            Assert.Equal("Hair", categories[1].GetProperty("name").GetString());
            Assert.Equal(2, categories[1].GetProperty("id").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_EquipmentSheetWithLocalAnimations_IsNotAlsoSpells()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 115, 3205, 4,
                (3205, new[] { 3205, 3206 }));
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            using var doc = JsonDocument.Parse(Build(root));
            var categories = doc.RootElement.GetProperty("sheets").GetProperty("115").GetProperty("categories");
            Assert.Equal(1, categories.GetArrayLength());
            Assert.Equal("Body", categories[0].GetProperty("name").GetString());
            Assert.Equal(115, doc.RootElement.GetProperty("animations")[0].GetProperty("ownerSheet").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_UnclaimedAnimatedSheet_IsSpells()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 200, 3205, 4,
                (3205, new[] { 3205 }));
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root));

            using var doc = JsonDocument.Parse(Build(root));
            var category = doc.RootElement.GetProperty("sheets").GetProperty("200").GetProperty("categories")[0];
            Assert.Equal("Spells", category.GetProperty("name").GetString());
            Assert.False(category.TryGetProperty("id", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_UnclaimedStaticSheet_IsTiles()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 300, 4000, 4);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root));

            using var doc = JsonDocument.Parse(Build(root));
            var category = doc.RootElement.GetProperty("sheets").GetProperty("300").GetProperty("categories")[0];
            Assert.Equal("Tiles", category.GetProperty("name").GetString());
            Assert.False(category.TryGetProperty("id", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_PreservesOrderedFrameIds()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 200, 3205, 4,
                (3205, new[] { 3205, 3207, 3206 }));
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root));

            using var doc = JsonDocument.Parse(Build(root));
            var frames = doc.RootElement.GetProperty("animations")[0].GetProperty("frames");
            Assert.Equal(3, frames.GetArrayLength());
            Assert.Equal(200, frames[0][0].GetInt32());
            Assert.Equal(3205, frames[0][1].GetInt32());
            Assert.Equal(3207, frames[1][1].GetInt32());
            Assert.Equal(3206, frames[2][1].GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_SameAnimationIdOnDifferentSheets_EmitsBoth()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 200, 3205, 4,
                (3205, new[] { 3205 }));
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 201, 5000, 4,
                (3205, new[] { 5000 }));
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root));

            using var doc = JsonDocument.Parse(Build(root));
            var animations = doc.RootElement.GetProperty("animations");
            Assert.Equal(2, animations.GetArrayLength());
            Assert.Equal(200, animations[0].GetProperty("ownerSheet").GetInt32());
            Assert.Equal(3205, animations[0].GetProperty("id").GetInt32());
            Assert.Equal(201, animations[1].GetProperty("ownerSheet").GetInt32());
            Assert.Equal(3205, animations[1].GetProperty("id").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_ReversedFileCreationOrder_ProducesByteIdenticalJson()
    {
        var rootA = AnimationSourceFixture.CreateDirectory();
        var rootB = AnimationSourceFixture.CreateDirectory();
        try
        {
            WriteSheet(rootA, 99);
            WriteSheet(rootA, 1000);
            WriteSheet(rootB, 1000);
            WriteSheet(rootB, 99);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(rootA),
                (AnimationType.Body, 1, new[] { 99 }));
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(rootB),
                (AnimationType.Body, 1, new[] { 99 }));

            var jsonA = Build(rootA);
            var jsonB = Build(rootB);
            Assert.Equal(jsonA, jsonB);

            using var doc = JsonDocument.Parse(jsonA);
            var sheetKeys = doc.RootElement.GetProperty("sheets")
                .EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "99", "1000" }, sheetKeys);
            var ownerSheets = doc.RootElement.GetProperty("animations")
                .EnumerateArray().Select(a => a.GetProperty("ownerSheet").GetInt32()).ToArray();
            Assert.Equal(new[] { 99, 1000 }, ownerSheets);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Fact]
    public void Build_SkipsNonGraphicAndNonNumericFiles()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 200, 3205, 4,
                (3205, new[] { 3205 }));
            AnimationSourceFixture.WriteSoundAdf(AnimationSourceFixture.DataDir(root), 300);
            File.WriteAllText(Path.Combine(AnimationSourceFixture.DataDir(root), "notes.adf"), "garbage");
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root));

            using var doc = JsonDocument.Parse(Build(root));
            var sheetKeys = doc.RootElement.GetProperty("sheets")
                .EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "200" }, sheetKeys);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Build(string root) =>
        AnimationManifestBuilder.Build(
            AnimationSourceFixture.DataDir(root),
            AnimationSourceFixture.CompiledEncPath(root));

    private static void WriteSheet(string root, int sheet) =>
        AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), sheet, 3205, 4,
            (3205, new[] { 3205 }));
}
