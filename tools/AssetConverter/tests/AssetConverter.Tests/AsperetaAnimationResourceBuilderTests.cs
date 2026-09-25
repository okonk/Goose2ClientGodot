using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
using Goose2.AssetConverter.SpriteFrames;
using AssetConverter.Tests.Fixtures;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaAnimationResourceBuilderTests
{
    [Fact]
    public void AllSevenCategories_UseExistingFolderMappingAndOffsetIds()
    {
        var (catalog, result) = Build(
            new[]
            {
                (1, 10, Indexes(Walk(0, 1, 100))),
                (2, 11, Indexes(Walk(0, 1, 101))),
                (3, 12, Indexes(Walk(0, 1, 102))),
                (4, 13, Indexes(Walk(0, 1, 103))),
                (5, 14, Indexes(Walk(0, 1, 104))),
                (6, 15, Indexes(Walk(0, 1, 105))),
                (7, 16, Indexes(Walk(0, 1, 106))),
            },
            Adf(1, new[] { Frame(100), Frame(101), Frame(102), Frame(103), Frame(104), Frame(105), Frame(106) }));

        Assert.Equal(7, result.Resources.Count);
        var expected = new (AnimationType Type, int Id, string Folder)[]
        {
            (AnimationType.Body, 10, "Bodies"),
            (AnimationType.Hair, 11, "Hair"),
            (AnimationType.Hand, 12, "Hands"),
            (AnimationType.Chest, 13, "Chest"),
            (AnimationType.Helm, 14, "Helms"),
            (AnimationType.Legs, 15, "Legs"),
            (AnimationType.Feet, 16, "Feet"),
        };
        foreach (var (type, id, folder) in expected)
        {
            var resource = result.Resources.Single(r => r.Type == type);
            int outputId = AsperetaSheets.BodyBase + id;
            Assert.Equal(outputId, resource.Id);
            Assert.Equal($"Assets/Sprites/{folder}/{outputId}/animations.tres", resource.RelativeOutputPath);
            Assert.Equal(AnimationNaming.ResourceRelativePath(type, outputId), resource.RelativeOutputPath);
        }
    }

    [Fact]
    public void FacingMapping_LeftIsSource3_Down2_Right1_Up0()
    {
        var (_, result) = Build(
            new[] { (1, 100, Indexes(
                Walk(0, 1, 1000), Walk(1, 1, 1001), Walk(2, 1, 1002), Walk(3, 1, 1003))) },
            Adf(1, new[] { Frame(1000), Frame(1001), Frame(1002), Frame(1003) }));

        var resource = Assert.Single(result.Resources);
        Assert.Equal(new[] { 1003 }, FrameIndexes(resource, "walk-no-equip-left"));
        Assert.Equal(new[] { 1002 }, FrameIndexes(resource, "walk-no-equip-down"));
        Assert.Equal(new[] { 1001 }, FrameIndexes(resource, "walk-no-equip-right"));
        Assert.Equal(new[] { 1000 }, FrameIndexes(resource, "walk-no-equip-up"));
    }

    [Fact]
    public void StatesMapToApprovedNames()
    {
        var (_, result) = Build(
            new[] { (1, 100, Indexes(
                Walk(0, 1, 2001), Walk(0, 2, 2002), Walk(0, 3, 2003), Walk(0, 4, 2004),
                Attack(0, 1, 3001), Attack(0, 2, 3002), Attack(0, 3, 3003), Attack(0, 4, 3004))) },
            Adf(0, Array.Empty<(int, int, int, int, int)>(), new[]
            {
                Anim(2001, new[] { 1001, 1002 }), Anim(2002, new[] { 1003, 1004 }),
                Anim(2003, new[] { 1005, 1006 }), Anim(2004, new[] { 1007, 1008 }),
                Anim(3001, new[] { 1009, 1010 }), Anim(3002, new[] { 1011, 1012 }),
                Anim(3003, new[] { 1013, 1014 }), Anim(3004, new[] { 1015, 1016 }),
            }),
            Adf(1, new[] { Frame(1001), Frame(1002), Frame(1003), Frame(1004), Frame(1005), Frame(1006), Frame(1007), Frame(1008),
                Frame(1009), Frame(1010), Frame(1011), Frame(1012), Frame(1013), Frame(1014), Frame(1015), Frame(1016) }));

        var names = Assert.Single(result.Resources).Animations.Select(a => a.Name).ToHashSet();
        foreach (var name in new[]
        {
            "walk-no-equip-up", "walk-asp-state-2-up", "walk-staff-up", "walk-1hand-up",
            "attack-no-equip-up", "attack-asp-state-2-up", "attack-staff-up", "attack-1hand-up",
        })
            Assert.Contains(name, names);
    }

    [Fact]
    public void EachWalkSourceCreatesMatchingIdleFromFrame0()
    {
        var (_, result) = Build(
            new[] { (1, 100, Indexes(
                Walk(0, 1, 2001), Walk(0, 2, 2002), Walk(0, 3, 2003), Walk(0, 4, 2004))) },
            Adf(0, Array.Empty<(int, int, int, int, int)>(), new[]
            {
                Anim(2001, new[] { 1001, 1002 }), Anim(2002, new[] { 1003, 1004 }),
                Anim(2003, new[] { 1005, 1006 }), Anim(2004, new[] { 1007, 1008 }),
            }),
            Adf(1, new[] { Frame(1001), Frame(1002), Frame(1003), Frame(1004), Frame(1005), Frame(1006), Frame(1007), Frame(1008) }));

        var resource = Assert.Single(result.Resources);
        Assert.Equal(new[] { 1001 }, FrameIndexes(resource, "idle-no-equip-up"));
        Assert.Equal(new[] { 1003 }, FrameIndexes(resource, "idle-asp-state-2-up"));
        Assert.Equal(new[] { 1005 }, FrameIndexes(resource, "idle-staff-up"));
        Assert.Equal(new[] { 1007 }, FrameIndexes(resource, "idle-1hand-up"));
    }

    [Fact]
    public void SourceFpsSurvivesSourceClipsIdlesAndAliases()
    {
        var (_, result) = Build(
            new[] { (1, 100, Indexes(
                Walk(0, 1, 2001), Walk(0, 2, 2002), Walk(0, 4, 2004), Attack(0, 1, 3001))) },
            Adf(0, Array.Empty<(int, int, int, int, int)>(), new[]
            {
                Anim(2001, new[] { 1001, 1002 }, 3), Anim(2002, new[] { 1003, 1004 }, 3),
                Anim(2004, new[] { 1005, 1006 }, 3), Anim(3001, new[] { 1007, 1008 }, 3),
            }),
            Adf(1, new[] { Frame(1001), Frame(1002), Frame(1003), Frame(1004), Frame(1005), Frame(1006), Frame(1007), Frame(1008) }));

        var resource = Assert.Single(result.Resources);
        Assert.NotEmpty(resource.Animations);
        Assert.All(resource.Animations, a => Assert.Equal(6f, a.Speed));
    }

    [Fact]
    public void EquipAliasesPreferState4Then3Then2PerDirection()
    {
        var (_, result) = Build(
            new[] { (1, 100, Indexes(
                Walk(0, 2, 2002), Walk(0, 3, 2003), Walk(0, 4, 2004),
                Walk(2, 2, 2005), Walk(2, 3, 2006),
                Walk(1, 2, 2007),
                Walk(3, 1, 2008), Walk(3, 4, 2009))) },
            Adf(0, Array.Empty<(int, int, int, int, int)>(), new[]
            {
                Anim(2002, new[] { 1002, 1003 }), Anim(2003, new[] { 1004, 1005 }), Anim(2004, new[] { 1006, 1007 }),
                Anim(2005, new[] { 1008, 1009 }), Anim(2006, new[] { 1010, 1011 }),
                Anim(2007, new[] { 1012, 1013 }),
                Anim(2008, new[] { 1014, 1015 }), Anim(2009, new[] { 1016, 1017 }),
            }),
            Adf(1, new[] { Frame(1002), Frame(1003), Frame(1004), Frame(1005), Frame(1006), Frame(1007), Frame(1008), Frame(1009),
                Frame(1010), Frame(1011), Frame(1012), Frame(1013), Frame(1014), Frame(1015), Frame(1016), Frame(1017) }));

        var resource = Assert.Single(result.Resources);
        Assert.Equal(new[] { 1006, 1007 }, FrameIndexes(resource, "walk-equip-up"));
        Assert.Equal(new[] { 1006 }, FrameIndexes(resource, "idle-equip-up"));
        Assert.Equal(new[] { 1010, 1011 }, FrameIndexes(resource, "walk-equip-down"));
        Assert.Equal(new[] { 1010 }, FrameIndexes(resource, "idle-equip-down"));
        Assert.Equal(new[] { 1012, 1013 }, FrameIndexes(resource, "walk-equip-right"));
        Assert.Equal(new[] { 1012 }, FrameIndexes(resource, "idle-equip-right"));
        Assert.Equal(new[] { 1016, 1017 }, FrameIndexes(resource, "walk-equip-left"));
        Assert.Equal(new[] { 1016 }, FrameIndexes(resource, "idle-equip-left"));
    }

    [Fact]
    public void State1CompatibilityAliasesRemain()
    {
        var (_, result) = Build(
            new[] { (1, 100, Indexes(Walk(0, 1, 2001), Attack(0, 1, 3001))) },
            Adf(0, Array.Empty<(int, int, int, int, int)>(), new[]
            {
                Anim(2001, new[] { 1001, 1002 }, 2), Anim(3001, new[] { 2000, 2001 }, 2),
            }),
            Adf(1, new[] { Frame(1001), Frame(1002), Frame(2000), Frame(2001) }));

        var resource = Assert.Single(result.Resources);
        Assert.Equal(new[] { 1001, 1002 }, FrameIndexes(resource, "walk-up"));
        Assert.Equal(4f, Animation(resource, "walk-up").Speed);
        Assert.Equal(new[] { 2000, 2001 }, FrameIndexes(resource, "attack-up"));
        Assert.Equal(4f, Animation(resource, "attack-up").Speed);
        Assert.Equal(new[] { 1001 }, FrameIndexes(resource, "idle-up"));
        Assert.Equal(4f, Animation(resource, "idle-up").Speed);
    }

    [Fact]
    public void ZeroSlotsCreateNothing()
    {
        var (catalog, result) = Build(
            new[]
            {
                (1, 200, new int[32]),
                (1, 201, Indexes(Walk(0, 1, 1000))),
            },
            Adf(1, new[] { Frame(1000) }));

        Assert.Equal(new[] { 10201 }, result.Resources.Select(r => r.Id).ToArray());
        Assert.Contains(result.Diagnostics, d => d.Contains("Body 200"));
        Assert.Empty(catalog.CompiledSlots.Where(s => s.ResourceId == 200));
    }

    [Fact]
    public void OneUnresolvedSlot_LeavesAllSiblingClipsPresent()
    {
        var (_, result) = Build(
            new[] { (1, 100, Indexes(
                Walk(0, 1, 2000), Walk(1, 1, 2001), Walk(2, 1, 2002), Walk(3, 1, 2003),
                Attack(0, 1, 3000), Attack(1, 1, 99999), Attack(2, 1, 3002), Attack(3, 1, 3003))) },
            Adf(0, Array.Empty<(int, int, int, int, int)>(), new[]
            {
                Anim(2000, new[] { 1000, 1001 }), Anim(2001, new[] { 1002, 1003 }),
                Anim(2002, new[] { 1004, 1005 }), Anim(2003, new[] { 1006, 1007 }),
                Anim(3000, new[] { 2000, 2001 }), Anim(3002, new[] { 2002, 2003 }), Anim(3003, new[] { 2004, 2005 }),
            }),
            Adf(1, new[] { Frame(1000), Frame(1001), Frame(1002), Frame(1003), Frame(1004), Frame(1005), Frame(1006), Frame(1007),
                Frame(2000), Frame(2001), Frame(2002), Frame(2003), Frame(2004), Frame(2005) }));

        var resource = Assert.Single(result.Resources);
        var names = resource.Animations.Select(a => a.Name).ToHashSet();
        foreach (var dir in new[] { "left", "down", "right", "up" })
        {
            Assert.Contains($"walk-no-equip-{dir}", names);
            Assert.Contains($"idle-no-equip-{dir}", names);
            Assert.Contains($"walk-{dir}", names);
        }
        Assert.Contains("attack-no-equip-up", names);
        Assert.Contains("attack-no-equip-down", names);
        Assert.Contains("attack-no-equip-left", names);
        Assert.Contains("attack-up", names);
        Assert.DoesNotContain("attack-no-equip-right", names);
        Assert.DoesNotContain("attack-right", names);
        Assert.Contains(result.Diagnostics, d => d.Contains("99999"));
    }

    [Fact]
    public void DirectFrameSlot_EmitsOneFrameAtEightFps()
    {
        var (_, result) = Build(
            new[] { (1, 100, Indexes(Walk(0, 1, 5000))) },
            Adf(1, new[] { Frame(5000) }));

        var resource = Assert.Single(result.Resources);
        var clip = Animation(resource, "walk-no-equip-up");
        Assert.Equal(new[] { 5000 }, clip.Frames.Select(f => f.Frame.Index).ToArray());
        Assert.Equal(8f, clip.Speed);
    }

    [Fact]
    public void MultiSheetAnimation_SerializesDistinctTextureResources()
    {
        var (_, result) = Build(
            new[] { (1, 100, Indexes(Walk(0, 1, 3000))) },
            Adf(0, Array.Empty<(int, int, int, int, int)>(), new[] { Anim(3000, new[] { 1000, 2000 }) }),
            Adf(1, new[] { Frame(1000) }),
            Adf(2, new[] { Frame(2000) }));

        var resource = Assert.Single(result.Resources);
        string tres = SpriteFramesWriter.Build(resource.Animations);

        Assert.Contains($"[ext_resource type=\"Texture2D\" path=\"res://Assets/Sprites/sheets/{AsperetaSheets.SheetBase}.png\"", tres);
        Assert.Contains($"[ext_resource type=\"Texture2D\" path=\"res://Assets/Sprites/sheets/{AsperetaSheets.SheetBase + 1}.png\"", tres);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(tres, "ext_resource type=\"Texture2D\"").Count);
    }

    [Fact]
    public void FirstFrameAndHeightKeys_UseOutputTypeAndOffsetId()
    {
        var (_, result) = Build(
            new[]
            {
                (1, 100, Indexes(Walk(2, 1, 1000))),
                (2, 101, Indexes(Walk(2, 1, 1001))),
            },
            Adf(1, new[] { Frame(1000, 0, 0, 24, 40), Frame(1001, 0, 0, 24, 64) }));

        var body = result.Resources.Single(r => r.Id == 10100);
        Assert.Equal(new AnimationFrameInfo(AsperetaSheets.SheetBase, 1000, 24, 40),
            body.AnimationToFirstFrame["Body-10100"]);
        Assert.Equal(40, body.AnimationHeights["Body-10100-walk-no-equip-down"]);
        Assert.Equal(40, body.AnimationHeights["Body-10100-idle-no-equip-down"]);
        Assert.Equal(40, body.AnimationHeights["Body-10100-walk-down"]);

        var hair = result.Resources.Single(r => r.Id == 10101);
        Assert.Equal(new AnimationFrameInfo(AsperetaSheets.SheetBase, 1001, 24, 64),
            hair.AnimationToFirstFrame["Hair-10101"]);
        Assert.Empty(hair.AnimationHeights);
    }

    [Fact]
    public void OrderingAndSerializedOutputAreDeterministic()
    {
        var (catalogA, resultA) = Build(
            new[] { (1, 100, Indexes(
                Walk(0, 1, 2001), Walk(0, 2, 2002), Walk(0, 4, 2004), Attack(0, 1, 3001))) },
            Adf(0, Array.Empty<(int, int, int, int, int)>(), new[]
            {
                Anim(2001, new[] { 1001, 1002 }), Anim(2002, new[] { 1003, 1004 }),
                Anim(2004, new[] { 1005, 1006 }), Anim(3001, new[] { 1007, 1008 }),
            }),
            Adf(1, new[] { Frame(1001), Frame(1002), Frame(1003), Frame(1004), Frame(1005), Frame(1006), Frame(1007), Frame(1008) }));
        var (_, resultB) = Build(
            new[] { (1, 100, Indexes(
                Walk(0, 1, 2001), Walk(0, 2, 2002), Walk(0, 4, 2004), Attack(0, 1, 3001))) },
            Adf(0, Array.Empty<(int, int, int, int, int)>(), new[]
            {
                Anim(2001, new[] { 1001, 1002 }), Anim(2002, new[] { 1003, 1004 }),
                Anim(2004, new[] { 1005, 1006 }), Anim(3001, new[] { 1007, 1008 }),
            }),
            Adf(1, new[] { Frame(1001), Frame(1002), Frame(1003), Frame(1004), Frame(1005), Frame(1006), Frame(1007), Frame(1008) }));

        Assert.Equal(resultA.Resources.Select(r => r.Animations.Select(a => a.Name)),
            resultB.Resources.Select(r => r.Animations.Select(a => a.Name)));
        Assert.Equal(
            resultA.Resources.Select(r => SpriteFramesWriter.Build(r.Animations)),
            resultB.Resources.Select(r => SpriteFramesWriter.Build(r.Animations)));
        Assert.Equal(catalogA.Diagnostics, resultA.Diagnostics);
    }

    [Fact]
    public void RealData_ResourcesCoverEveryResolvableCompiledSlot()
    {
        var catalog = AsperetaAnimationCatalog.Load(Paths.AsperetaData, Paths.AsperetaCompiledEnc);
        var result = AsperetaAnimationResourceBuilder.Build(catalog);

        Assert.Equal(243, catalog.CompiledEntries.Count);

        var byEntry = result.Resources.ToDictionary(r => (r.Type, r.Id - AsperetaSheets.BodyBase));
        int expectedResources = 0;
        foreach (var entry in catalog.CompiledEntries)
        {
            var slots = catalog.CompiledSlots
                .Where(s => s.Type == entry.Type && s.ResourceId == entry.Id)
                .ToList();
            var resolvable = slots.Where(s => s.Resolution is not null).ToList();
            if (resolvable.Count == 0)
            {
                Assert.False(byEntry.ContainsKey((entry.Type, entry.Id)),
                    $"{entry.Type} {entry.Id} has no resolvable slots but produced a resource");
                continue;
            }

            expectedResources++;
            var names = byEntry[(entry.Type, entry.Id)].Animations.Select(a => a.Name).ToHashSet();
            foreach (var slot in resolvable)
            {
                string baseName = slot.Motion switch
                {
                    AsperetaMotion.Walk => slot.State switch
                    {
                        1 => "walk-no-equip",
                        2 => "walk-asp-state-2",
                        3 => "walk-staff",
                        _ => "walk-1hand",
                    },
                    _ => slot.State switch
                    {
                        1 => "attack-no-equip",
                        2 => "attack-asp-state-2",
                        3 => "attack-staff",
                        _ => "attack-1hand",
                    },
                };
                string dirName = slot.Facing switch
                {
                    0 => "up",
                    1 => "right",
                    2 => "down",
                    _ => "left",
                };
                Assert.True(names.Contains($"{baseName}-{dirName}"),
                    $"{entry.Type} {entry.Id} missing {baseName}-{dirName}");
            }
        }
        Assert.Equal(expectedResources, result.Resources.Count);

        Assert.Equal(4, catalog.CompiledSlots.Count(s => s.ReferenceId == 1 && s.Resolution is null));
        Assert.Contains(result.Diagnostics, d => d.Contains("reference 1 not found"));
    }

    private static (AsperetaAnimationCatalog, AsperetaAnimationBuildResult) Build(
        (int RawType, int Id, int[] Indexes)[] entries,
        params (int File, (int Index, int X, int Y, int W, int H)[] Frames, (int Id, int[] FrameIds, int Interval)[] Anims)[] adfs)
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            foreach (var (file, frames, anims) in adfs)
                AnimationSourceFixture.WriteAsperetaAdf(data, file, frames, anims);
            var enc = AnimationSourceFixture.AsperetaCompiledEncPath(root);
            AnimationSourceFixture.WriteAsperetaCompiledEncRaw(enc, entries);
            var catalog = AsperetaAnimationCatalog.Load(data, enc);
            return (catalog, AsperetaAnimationResourceBuilder.Build(catalog));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int, (int, int, int, int, int)[], (int, int[], int)[]) Adf(
        int file, (int Index, int X, int Y, int W, int H)[] frames,
        (int Id, int[] FrameIds, int Interval)[]? anims = null)
        => (file, frames, anims ?? Array.Empty<(int, int[], int)>());

    private static (int, int, int, int, int) Frame(int index, int x = 0, int y = 0, int w = 24, int h = 48)
        => (index, x, y, w, h);

    private static (int Id, int[] FrameIds, int Interval) Anim(int id, int[] frameIds, int interval = 0)
        => (id, frameIds, interval);

    private static int[] Indexes(params (AsperetaMotion Motion, int Facing, int State, int Ref)[] refs)
    {
        var indexes = new int[32];
        foreach (var (motion, facing, state, @ref) in refs)
            indexes[(motion == AsperetaMotion.Walk ? 0 : 16) + facing * 4 + (state - 1)] = @ref;
        return indexes;
    }

    private static (AsperetaMotion, int, int, int) Walk(int facing, int state, int @ref)
        => (AsperetaMotion.Walk, facing, state, @ref);

    private static (AsperetaMotion, int, int, int) Attack(int facing, int state, int @ref)
        => (AsperetaMotion.Attack, facing, state, @ref);

    private static SpriteFramesAnimationSpec Animation(CompiledSpriteFramesResource resource, string name)
        => resource.Animations.Single(a => a.Name == name);

    private static int[] FrameIndexes(CompiledSpriteFramesResource resource, string name)
        => Animation(resource, name).Frames.Select(f => f.Frame.Index).ToArray();
}
