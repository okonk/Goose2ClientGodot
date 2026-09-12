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

    private static string BuildCombined(string root) =>
        AnimationManifestBuilder.BuildCombined(
            AnimationSourceFixture.DataDir(root),
            AnimationSourceFixture.CompiledEncPath(root),
            AnimationSourceFixture.AsperetaDataDir(root),
            AnimationSourceFixture.AsperetaCompiledEncPath(root));

    private static void WriteIllutia(string root) =>
        AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 115, 3205, 4,
            (3205, new[] { 3205, 3206 }));

    private static JsonElement FindAnimation(JsonElement animations, int id)
    {
        foreach (var animation in animations.EnumerateArray())
            if (animation.GetProperty("id").GetInt32() == id)
                return animation;
        throw new InvalidOperationException($"animation {id} not found");
    }

    [Fact]
    public void BuildCombined_RanksValidGraphicSheets_SkipsMalformedSoundAndEmpty()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            WriteIllutia(root);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            var aspData = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1200, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 2,
                new[] { (1300, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaMalformedAdf(aspData, 3);
            AnimationSourceFixture.WriteAsperetaSoundAdf(aspData, 4);
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 5,
                Array.Empty<(int, int, int, int, int)>(), Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 6,
                new[] { (1400, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var sheetKeys = doc.RootElement.GetProperty("sheets")
                .EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "115", "20000", "20001", "20002" }, sheetKeys);

            var category = doc.RootElement.GetProperty("sheets").GetProperty("20000").GetProperty("categories")[0];
            Assert.Equal("Tiles", category.GetProperty("name").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_NormalizesEveryGraphicIdToGraphicBase()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            WriteIllutia(root);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            var aspData = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1200, 0, 0, 24, 48), (1201, 24, 0, 24, 48) },
                new[] { (500, new[] { 1200, 1201 }) });
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var animation = FindAnimation(doc.RootElement.GetProperty("animations"), 700500);
            Assert.Equal(20000, animation.GetProperty("ownerSheet").GetInt32());
            var frames = animation.GetProperty("frames");
            Assert.Equal(2, frames.GetArrayLength());
            Assert.Equal(20000, frames[0][0].GetInt32());
            Assert.Equal(701200, frames[0][1].GetInt32());
            Assert.Equal(701201, frames[1][1].GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_AssignsBodyCategoryToEverySheetReachedByMonster()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            WriteIllutia(root);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            var aspData = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (3000, new[] { 5000, 5001 }) });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (5000, 0, 0, 24, 48), (5001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 2,
                new[] { (6000, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());

            var indexes = new int[32];
            indexes[0] = 3000;
            indexes[16] = 6000;
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root),
                (AnimationType.Body, 200, indexes));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var sheets = doc.RootElement.GetProperty("sheets");

            var body1 = sheets.GetProperty("20000").GetProperty("categories")[0];
            Assert.Equal("Body", body1.GetProperty("name").GetString());
            Assert.Equal(10200, body1.GetProperty("id").GetInt32());

            var body2 = sheets.GetProperty("20001").GetProperty("categories")[0];
            Assert.Equal("Body", body2.GetProperty("name").GetString());
            Assert.Equal(10200, body2.GetProperty("id").GetInt32());

            Assert.Equal(20000, FindAnimation(doc.RootElement.GetProperty("animations"), 3000).GetProperty("ownerSheet").GetInt32());
            Assert.Equal(20001, FindAnimation(doc.RootElement.GetProperty("animations"), 6000).GetProperty("ownerSheet").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_AssignsSpellsToFrameSheet_NotDefinitionSheet()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            WriteIllutia(root);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            var aspData = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (500, new[] { 1200, 1201 }) });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1200, 0, 0, 24, 48), (1201, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var category = doc.RootElement.GetProperty("sheets").GetProperty("20000").GetProperty("categories")[0];
            Assert.Equal("Spells", category.GetProperty("name").GetString());
            Assert.False(category.TryGetProperty("id", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_StandaloneEffect_UsesNormalizedOwnerSheetAndFrames()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            WriteIllutia(root);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            var aspData = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (9999, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 3,
                new[] { (1200, 0, 0, 24, 48), (1201, 24, 0, 24, 48) },
                new[] { (500, new[] { 1200, 1201 }) });
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var animation = FindAnimation(doc.RootElement.GetProperty("animations"), 700500);
            Assert.Equal(20001, animation.GetProperty("ownerSheet").GetInt32());
            var frame = animation.GetProperty("frames")[0];
            Assert.Equal(20001, frame[0].GetInt32());
            Assert.Equal(701200, frame[1].GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_CrossSheetAnimation_PreservesOrderAndEmitsBothSheets()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            WriteIllutia(root);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            var aspData = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (500, new[] { 1000, 2000, 1001 }) });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 2,
                new[] { (2000, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var animation = FindAnimation(doc.RootElement.GetProperty("animations"), 700500);
            var frames = animation.GetProperty("frames");
            Assert.Equal(3, frames.GetArrayLength());
            Assert.Equal(20000, frames[0][0].GetInt32());
            Assert.Equal(701000, frames[0][1].GetInt32());
            Assert.Equal(20001, frames[1][0].GetInt32());
            Assert.Equal(702000, frames[1][1].GetInt32());
            Assert.Equal(20000, frames[2][0].GetInt32());
            Assert.Equal(701001, frames[2][1].GetInt32());

            var sheets = doc.RootElement.GetProperty("sheets");
            Assert.Equal("Spells", sheets.GetProperty("20000").GetProperty("categories")[0].GetProperty("name").GetString());
            Assert.Equal("Spells", sheets.GetProperty("20001").GetProperty("categories")[0].GetProperty("name").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_DuplicateSourceFrameIdAcrossSheets_ThrowsDeterministically()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            WriteIllutia(root);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            var aspData = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1200, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 2,
                new[] { (1200, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            var ex = Assert.Throws<InvalidOperationException>(() => BuildCombined(root));
            Assert.Contains("1200", ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_EveryEmittedFrame_ExistsInCombinedFrameManifest()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            WriteIllutia(root);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            var aspData = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (500, new[] { 5000, 6000 }), (100, new[] { 5001, 5002 }) });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (5000, 0, 0, 24, 48), (5001, 24, 0, 24, 48), (5002, 48, 0, 24, 48) },
                Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 2,
                new[] { (6000, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());

            var indexes = new int[32];
            indexes[0] = 100;
            indexes[16] = 6000;
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root),
                (AnimationType.Body, 200, indexes));

            using var animationDoc = JsonDocument.Parse(BuildCombined(root));
            using var frameDoc = JsonDocument.Parse(FrameManifestBuilder.BuildCombined(
                AnimationSourceFixture.DataDir(root),
                AnimationSourceFixture.AsperetaDataDir(root)));
            var frameSheets = frameDoc.RootElement.GetProperty("sheets");

            foreach (var animation in animationDoc.RootElement.GetProperty("animations").EnumerateArray())
            {
                foreach (var frame in animation.GetProperty("frames").EnumerateArray())
                {
                    var sheet = frame[0].GetInt32().ToString();
                    var graphic = frame[1].GetInt32().ToString();
                    Assert.True(frameSheets.TryGetProperty(sheet, out var frames), $"missing sheet {sheet}");
                    Assert.True(frames.TryGetProperty(graphic, out _), $"missing frame {graphic} on sheet {sheet}");
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
