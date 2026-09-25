using System.Text.Json;
using AssetConverter.Tests.Fixtures;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
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
    public void Build_DuplicateCompiledMapping_IsEmittedOnce()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 115, 3205, 4);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }),
                (AnimationType.Body, 1, new[] { 115 }));

            using var doc = JsonDocument.Parse(Build(root));
            var categories = doc.RootElement.GetProperty("sheets").GetProperty("115").GetProperty("categories");
            Assert.Equal(1, categories.GetArrayLength());
            Assert.Equal("Body", categories[0].GetProperty("name").GetString());
            Assert.Equal(1, categories[0].GetProperty("id").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_CompiledSheetsWithoutGraphicAdf_AreNotEmitted()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 115, 3205, 4);
            AnimationSourceFixture.WriteSoundAdf(AnimationSourceFixture.DataDir(root), 6746);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }),
                (AnimationType.Helm, 2, new[] { 6746, 6747 }));

            using var doc = JsonDocument.Parse(Build(root));
            var sheetKeys = doc.RootElement.GetProperty("sheets")
                .EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "115" }, sheetKeys);
            var categories = doc.RootElement.GetProperty("sheets").GetProperty("115").GetProperty("categories");
            Assert.Equal(1, categories.GetArrayLength());
            Assert.Equal("Body", categories[0].GetProperty("name").GetString());
            Assert.Equal(1, categories[0].GetProperty("id").GetInt32());
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
    public void Build_ItemSheetWithoutEncCategory_GetsFallbackPlusItemTiles()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 200, 3205, 4,
                (3205, new[] { 3205 }));
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 300, 4000, 4);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root));

            using var doc = JsonDocument.Parse(Build(root, new[] { 200, 300 }));
            var sheets = doc.RootElement.GetProperty("sheets");
            Assert.Equal(new[] { "ItemTiles", "Spells" }, Names(sheets, 200));
            Assert.Equal(new[] { "ItemTiles", "Tiles" }, Names(sheets, 300));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_ItemSheetWithEncCategory_GetsBothInNameOrder()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 115, 3205, 4);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                (AnimationType.Body, 1, new[] { 115 }));

            using var doc = JsonDocument.Parse(Build(root, new[] { 115 }));
            var categories = doc.RootElement.GetProperty("sheets").GetProperty("115").GetProperty("categories");
            Assert.Equal(2, categories.GetArrayLength());
            Assert.Equal("Body", categories[0].GetProperty("name").GetString());
            Assert.Equal(1, categories[0].GetProperty("id").GetInt32());
            Assert.Equal("ItemTiles", categories[1].GetProperty("name").GetString());
            Assert.False(categories[1].TryGetProperty("id", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_SheetNotInItemSet_DoesNotGetItemTiles()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 300, 4000, 4);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root));

            using var doc = JsonDocument.Parse(Build(root, new[] { 999 }));
            Assert.Equal(new[] { "Tiles" }, Names(doc.RootElement.GetProperty("sheets"), 300));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_ItemSheetsAbsentFromSheetSet_AreIgnored()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 300, 4000, 4);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root));

            using var doc = JsonDocument.Parse(Build(root, new[] { 300, 20107, 20444 }));
            var sheetKeys = doc.RootElement.GetProperty("sheets")
                .EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "300" }, sheetKeys);
            Assert.Equal(new[] { "ItemTiles", "Tiles" }, Names(doc.RootElement.GetProperty("sheets"), 300));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_MissingItemTileSheetsFile_ThrowsNamingPath()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var path = Path.Combine(root, "missing.json");
            var ex = Assert.Throws<FileNotFoundException>(() =>
                AnimationManifestBuilder.Build(
                    AnimationSourceFixture.DataDir(root),
                    AnimationSourceFixture.CompiledEncPath(root),
                    path));
            Assert.Contains(path, ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_MalformedItemTileSheetsFile_ThrowsNamingPath()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var path = Path.Combine(root, "item-tile-sheets.json");
            File.WriteAllText(path, "not json");
            var ex = Assert.Throws<InvalidOperationException>(() =>
                AnimationManifestBuilder.Build(
                    AnimationSourceFixture.DataDir(root),
                    AnimationSourceFixture.CompiledEncPath(root),
                    path));
            Assert.Contains(path, ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_EmptyItemTileSheets_NoSheetGetsItemTiles()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), 300, 4000, 4);
            AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root));

            using var doc = JsonDocument.Parse(Build(root, Array.Empty<int>()));
            Assert.Equal(new[] { "Tiles" }, Names(doc.RootElement.GetProperty("sheets"), 300));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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

    private static string Build(string root, int[] itemSheets = null) =>
        AnimationManifestBuilder.Build(
            AnimationSourceFixture.DataDir(root),
            AnimationSourceFixture.CompiledEncPath(root),
            WriteItemTileSheets(root, itemSheets));

    private static string WriteItemTileSheets(string root, int[] itemSheets)
    {
        var path = Path.Combine(root, "item-tile-sheets.json");
        File.WriteAllText(path, JsonSerializer.Serialize(itemSheets ?? Array.Empty<int>()));
        return path;
    }

    private static string[] Names(JsonElement sheets, int sheet)
    {
        var categories = sheets.GetProperty(sheet.ToString()).GetProperty("categories");
        return categories.EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()).ToArray();
    }

    private static (string Name, int? Id)[] CategoryPairs(JsonElement sheets, int sheet)
    {
        var categories = sheets.GetProperty(sheet.ToString()).GetProperty("categories");
        var pairs = new List<(string Name, int? Id)>(categories.GetArrayLength());
        foreach (var category in categories.EnumerateArray())
        {
            int? id = category.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : null;
            pairs.Add((category.GetProperty("name").GetString()!, id));
        }
        return pairs.ToArray();
    }

    private static void WriteSheet(string root, int sheet) =>
        AnimationSourceFixture.WriteAdf(AnimationSourceFixture.DataDir(root), sheet, 3205, 4,
            (3205, new[] { 3205 }));

    private static string BuildCombined(string root, int[] itemSheets = null) =>
        AnimationManifestBuilder.BuildCombined(
            AnimationSourceFixture.DataDir(root),
            AnimationSourceFixture.CompiledEncPath(root),
            AnimationSourceFixture.AsperetaDataDir(root),
            AnimationSourceFixture.AsperetaCompiledEncPath(root),
            WriteItemTileSheets(root, itemSheets));

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
    public void BuildCombined_TagsItemSheetsInBothDataSets()
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
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            using var doc = JsonDocument.Parse(BuildCombined(root, new[] { 115, 20000 }));
            var sheets = doc.RootElement.GetProperty("sheets");
            Assert.Equal(new[] { "Body", "ItemTiles" }, Names(sheets, 115));
            Assert.Equal(new[] { "ItemTiles", "Tiles" }, Names(sheets, 20000));
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

    [Fact]
    public void BuildCombined_CategoriesCoverEveryCompiledTypeResourceAndState()
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
                new[] { (100, new[] { 1000, 1001 }), (200, new[] { 2000, 2001 }), (300, new[] { 3000, 3001 }) });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 2,
                new[] { (2000, 0, 0, 24, 48), (2001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 3,
                new[] { (3000, 0, 0, 24, 48), (3001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());

            var body = new int[32];
            body[0] = 100;
            body[2] = 200;
            body[18] = 300;
            var hair = new int[32];
            hair[1] = 100;
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root),
                (AnimationType.Body, 200, body),
                (AnimationType.Hair, 300, hair));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var sheets = doc.RootElement.GetProperty("sheets");
            Assert.Equal(new (string, int?)[] { ("Body", 10200), ("Hair", 10300) }, CategoryPairs(sheets, 20000));
            Assert.Equal(new (string, int?)[] { ("Body", 10200) }, CategoryPairs(sheets, 20001));
            Assert.Equal(new (string, int?)[] { ("Body", 10200) }, CategoryPairs(sheets, 20002));

            var animations = doc.RootElement.GetProperty("animations").EnumerateArray()
                .Select(a => a.GetProperty("id").GetInt32()).ToArray();
            Assert.Contains(100, animations);
            Assert.Contains(200, animations);
            Assert.Contains(300, animations);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_UsesDefinitionIntervalFpsAndEightForDirectFramesAndIllutia()
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
                new[]
                {
                    (10, new[] { 1000, 1001 }, 3),
                    (20, new[] { 1000, 1001 }, 5),
                    (30, new[] { 1000, 1001 }, 0),
                    (40, new[] { 1000, 1001 }, 10),
                });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 2,
                new[] { (4000, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());

            var indexes = new int[32];
            indexes[0] = 4000;
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root),
                (AnimationType.Body, 200, indexes));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var animations = doc.RootElement.GetProperty("animations");
            Assert.Equal(6, FindAnimation(animations, 700010).GetProperty("fps").GetInt32());
            Assert.Equal(10, FindAnimation(animations, 700020).GetProperty("fps").GetInt32());
            Assert.Equal(8, FindAnimation(animations, 700030).GetProperty("fps").GetInt32());
            Assert.Equal(20, FindAnimation(animations, 700040).GetProperty("fps").GetInt32());
            Assert.Equal(8, FindAnimation(animations, 4000).GetProperty("fps").GetInt32());
            Assert.Equal(8, FindAnimation(animations, 3205).GetProperty("fps").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_EmitsEveryUnclaimedDefinition_IncludingFormerOutOfRangeIds()
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
                new[]
                {
                    (500, new[] { 1000, 1001 }),
                    (55228, new[] { 1001, 1002 }),
                    (95170, new[] { 1002, 1003 }),
                    (115500, new[] { 1003, 1004 }),
                });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[]
                {
                    (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48),
                    (1002, 48, 0, 24, 48), (1003, 72, 0, 24, 48), (1004, 96, 0, 24, 48),
                }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var animations = doc.RootElement.GetProperty("animations").EnumerateArray()
                .Select(a => a.GetProperty("id").GetInt32()).ToArray();
            Assert.Contains(700500, animations);
            Assert.Contains(755228, animations);
            Assert.Contains(795170, animations);
            Assert.Contains(815500, animations);
            Assert.Equal(new (string, int?)[] { ("Spells", null) },
                CategoryPairs(doc.RootElement.GetProperty("sheets"), 20000));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_ClaimedUnresolvedIds_DoNotReappearAsEffects()
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
                new[] { (115500, new[] { 1000, 1001 }), (88888, new[] { 77777, 77778 }) });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());

            var body200 = new int[32];
            body200[0] = 99999;
            body200[1] = 115500;
            var body201 = new int[32];
            body201[0] = 88888;
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root),
                (AnimationType.Body, 200, body200),
                (AnimationType.Body, 201, body201));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var animations = doc.RootElement.GetProperty("animations").EnumerateArray()
                .Select(a => a.GetProperty("id").GetInt32()).ToArray();
            Assert.Contains(115500, animations);
            Assert.DoesNotContain(7115500, animations);
            Assert.DoesNotContain(99999, animations);
            Assert.DoesNotContain(799999, animations);
            Assert.DoesNotContain(88888, animations);
            Assert.DoesNotContain(788888, animations);
            Assert.Equal(new (string, int?)[] { ("Body", 10200) },
                CategoryPairs(doc.RootElement.GetProperty("sheets"), 20000));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_SharedSourceReference_EmitsOneRecordWithAllOwningCategories()
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
                new[] { (3000, new[] { 1000, 1001 }) });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());

            var body200 = new int[32];
            body200[0] = 3000;
            var body201 = new int[32];
            body201[16] = 3000;
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root),
                (AnimationType.Body, 200, body200),
                (AnimationType.Body, 201, body201));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var matches = doc.RootElement.GetProperty("animations").EnumerateArray()
                .Count(a => a.GetProperty("id").GetInt32() == 3000);
            Assert.Equal(1, matches);
            Assert.Equal(20000, FindAnimation(doc.RootElement.GetProperty("animations"), 3000).GetProperty("ownerSheet").GetInt32());
            Assert.Equal(new (string, int?)[] { ("Body", 10200), ("Body", 10201) },
                CategoryPairs(doc.RootElement.GetProperty("sheets"), 20000));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_ClaimedOnlySheet_HasNoSpells_UntouchedSheetIsTiles()
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
                new[] { (3000, new[] { 1000, 1001 }) });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 2,
                new[] { (9000, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());

            var indexes = new int[32];
            indexes[0] = 3000;
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root),
                (AnimationType.Body, 200, indexes));

            using var doc = JsonDocument.Parse(BuildCombined(root));
            var sheets = doc.RootElement.GetProperty("sheets");
            Assert.Equal(new (string, int?)[] { ("Body", 10200) }, CategoryPairs(sheets, 20000));
            Assert.Equal(new (string, int?)[] { ("Tiles", null) }, CategoryPairs(sheets, 20001));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_CatalogOverload_MatchesStringOverload_AndDoesNotMutateCatalog()
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
                new[] { (500, new[] { 1000, 1001 }) });
            AnimationSourceFixture.WriteAsperetaAdf(aspData, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());

            var indexes = new int[32];
            indexes[0] = 500;
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root),
                (AnimationType.Body, 200, indexes));

            var catalog = AsperetaAnimationCatalog.Load(
                AnimationSourceFixture.AsperetaDataDir(root),
                AnimationSourceFixture.AsperetaCompiledEncPath(root));
            var resolvedBefore = catalog.Resolved.ToDictionary(r => r.Key, r => r.Value.Fps);
            var unclaimedBefore = catalog.UnclaimedResolvedIds.ToArray();
            var diagnosticsBefore = catalog.Diagnostics.ToArray();

            var viaCatalog = AnimationManifestBuilder.BuildCombined(
                AnimationSourceFixture.DataDir(root),
                AnimationSourceFixture.CompiledEncPath(root),
                catalog,
                WriteItemTileSheets(root, null));
            var againViaCatalog = AnimationManifestBuilder.BuildCombined(
                AnimationSourceFixture.DataDir(root),
                AnimationSourceFixture.CompiledEncPath(root),
                catalog,
                WriteItemTileSheets(root, null));

            Assert.Equal(BuildCombined(root), viaCatalog);
            Assert.Equal(viaCatalog, againViaCatalog);
            Assert.Equal(resolvedBefore, catalog.Resolved.ToDictionary(r => r.Key, r => r.Value.Fps));
            Assert.Equal(unclaimedBefore, catalog.UnclaimedResolvedIds.ToArray());
            Assert.Equal(diagnosticsBefore, catalog.Diagnostics.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCombined_ReversedAsperetaFileOrder_ProducesByteIdenticalJson()
    {
        var rootA = AnimationSourceFixture.CreateDirectory();
        var rootB = AnimationSourceFixture.CreateDirectory();
        try
        {
            foreach (var root in new[] { rootA, rootB })
            {
                WriteIllutia(root);
                AnimationSourceFixture.WriteCompiledEnc(AnimationSourceFixture.CompiledEncPath(root),
                    (AnimationType.Body, 1, new[] { 115 }));
            }

            var filesA = new (int Number, (int Index, int X, int Y, int W, int H)[] Frames, (int Id, int[] FrameIds)[] Anims)[]
            {
                (0, Array.Empty<(int, int, int, int, int)>(), new[] { (500, new[] { 1000, 2000 }) }),
                (1, new[] { (1000, 0, 0, 24, 48) }, Array.Empty<(int, int[])>()),
                (2, new[] { (2000, 0, 0, 24, 48) }, Array.Empty<(int, int[])>()),
            };
            var aspDataA = AnimationSourceFixture.AsperetaDataDir(rootA);
            var aspDataB = AnimationSourceFixture.AsperetaDataDir(rootB);
            foreach (var (number, frames, anims) in filesA)
                AnimationSourceFixture.WriteAsperetaAdf(aspDataA, number, frames, anims);
            foreach (var (number, frames, anims) in filesA.Reverse())
                AnimationSourceFixture.WriteAsperetaAdf(aspDataB, number, frames, anims);
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(rootA));
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(rootB));

            Assert.Equal(BuildCombined(rootA), BuildCombined(rootB));

            using var doc = JsonDocument.Parse(BuildCombined(rootA));
            var animation = FindAnimation(doc.RootElement.GetProperty("animations"), 700500);
            var frameArray = animation.GetProperty("frames");
            Assert.Equal(20000, frameArray[0][0].GetInt32());
            Assert.Equal(701000, frameArray[0][1].GetInt32());
            Assert.Equal(20001, frameArray[1][0].GetInt32());
            Assert.Equal(702000, frameArray[1][1].GetInt32());
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }
}
