using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
using AssetConverter.Tests.Fixtures;

namespace AssetConverter.Tests;

public class AsperetaAnimationCatalogTests
{
    [Fact]
    public void Load_RanksSheetsByFileNumber_NotCreationOrder()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 3,
                new[] { (300, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (100, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(data, 2,
                new[] { (200, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            var catalog = AsperetaAnimationCatalog.Load(data, AnimationSourceFixture.AsperetaCompiledEncPath(root));

            Assert.Equal(3, catalog.Sheets.Count);
            Assert.Equal(AsperetaSheets.SheetBase, catalog.Sheets[1].NewSheetNumber);
            Assert.Equal(AsperetaSheets.SheetBase + 1, catalog.Sheets[2].NewSheetNumber);
            Assert.Equal(AsperetaSheets.SheetBase + 2, catalog.Sheets[3].NewSheetNumber);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_SkipsMalformedSoundAndFramelessSheets()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (100, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaMalformedAdf(data, 2);
            AnimationSourceFixture.WriteAsperetaSoundAdf(data, 3);
            AnimationSourceFixture.WriteAsperetaAdf(data, 4,
                Array.Empty<(int, int, int, int, int)>(), Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(data, 5,
                new[] { (500, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            var catalog = AsperetaAnimationCatalog.Load(data, AnimationSourceFixture.AsperetaCompiledEncPath(root));

            Assert.Equal(new[] { 1, 5 }, catalog.Sheets.Keys.ToArray());
            Assert.Equal(AsperetaSheets.SheetBase, catalog.Sheets[1].NewSheetNumber);
            Assert.Equal(AsperetaSheets.SheetBase + 1, catalog.Sheets[5].NewSheetNumber);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompiledSlots_CoverAllSevenTypesAndAllThirtyTwoSlots()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            Directory.CreateDirectory(data);
            var types = new[]
            {
                AnimationType.Body, AnimationType.Hair, AnimationType.Hand, AnimationType.Chest,
                AnimationType.Helm, AnimationType.Legs, AnimationType.Feet,
            };
            var records = new (int RawType, int Id, int[] Indexes)[7];
            for (int i = 0; i < 7; i++)
                records[i] = (i + 1, 10 + i, Enumerable.Range(1000 * (i + 1), 32).ToArray());
            var enc = AnimationSourceFixture.AsperetaCompiledEncPath(root);
            AnimationSourceFixture.WriteAsperetaCompiledEncRaw(enc, records);

            var catalog = AsperetaAnimationCatalog.Load(data, enc);

            Assert.Equal(7, catalog.CompiledEntries.Count);
            Assert.Equal(types, catalog.CompiledEntries.Select(e => e.Type).ToArray());
            Assert.Equal(7 * 32, catalog.CompiledSlots.Count);

            var expected = new List<AsperetaCompiledSlot>();
            for (int i = 0; i < 7; i++)
                for (int slot = 0; slot < 32; slot++)
                {
                    int slotBase = slot % 16;
                    expected.Add(new AsperetaCompiledSlot(
                        types[i], 10 + i,
                        slot < 16 ? AsperetaMotion.Walk : AsperetaMotion.Attack,
                        slotBase % 4 + 1, slotBase / 4,
                        1000 * (i + 1) + slot, null));
                }
            Assert.Equal(expected, catalog.CompiledSlots);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompiledSlots_OmitZeroSlots()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            Directory.CreateDirectory(data);
            var indexes = new int[32];
            indexes[0] = 11;
            indexes[7] = 22;
            indexes[16] = 33;
            indexes[31] = 44;
            var enc = AnimationSourceFixture.AsperetaCompiledEncPath(root);
            AnimationSourceFixture.WriteAsperetaCompiledEncRaw(enc, (1, 5, indexes));

            var catalog = AsperetaAnimationCatalog.Load(data, enc);

            Assert.Equal(new[] { 11, 22, 33, 44 }, catalog.CompiledSlots.Select(s => s.ReferenceId).ToArray());
            Assert.All(catalog.CompiledSlots, s =>
            {
                Assert.Equal(AnimationType.Body, s.Type);
                Assert.Equal(5, s.ResourceId);
            });
            Assert.Equal((AsperetaMotion.Walk, 1, 0), (catalog.CompiledSlots[0].Motion, catalog.CompiledSlots[0].State, catalog.CompiledSlots[0].Facing));
            Assert.Equal((AsperetaMotion.Walk, 4, 1), (catalog.CompiledSlots[1].Motion, catalog.CompiledSlots[1].State, catalog.CompiledSlots[1].Facing));
            Assert.Equal((AsperetaMotion.Attack, 1, 0), (catalog.CompiledSlots[2].Motion, catalog.CompiledSlots[2].State, catalog.CompiledSlots[2].Facing));
            Assert.Equal((AsperetaMotion.Attack, 4, 3), (catalog.CompiledSlots[3].Motion, catalog.CompiledSlots[3].State, catalog.CompiledSlots[3].Facing));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvedAnimation_DerivesFpsFromInterval()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (500, new[] { 1000, 1001 }, 3), (600, new[] { 1002, 1003 }, 5) });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48), (1002, 48, 0, 24, 48), (1003, 72, 0, 24, 48) },
                Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            var catalog = AsperetaAnimationCatalog.Load(data, AnimationSourceFixture.AsperetaCompiledEncPath(root));

            Assert.Equal(6, catalog.Resolved[500].Fps);
            Assert.Equal(10, catalog.Resolved[600].Fps);
            Assert.False(catalog.Resolved[500].DirectFrame);
            Assert.Equal(500, catalog.Resolved[500].SourceId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompiledSlot_WithoutDefinition_FallsBackToDirectFrameAtEightFps()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1200, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            var indexes = new int[32];
            indexes[0] = 1200;
            var enc = AnimationSourceFixture.AsperetaCompiledEncPath(root);
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc, (AnimationType.Body, 200, indexes));

            var catalog = AsperetaAnimationCatalog.Load(data, enc);

            var slot = Assert.Single(catalog.CompiledSlots);
            var resolved = slot.Resolution;
            Assert.NotNull(resolved);
            Assert.Equal(8, resolved!.Fps);
            Assert.True(resolved.DirectFrame);
            Assert.Equal(1200, resolved.SourceId);
            var frame = Assert.Single(resolved.Frames);
            Assert.Equal(AsperetaSheets.SheetBase, frame.Sheet);
            Assert.Equal(1200, frame.Frame.Index);
            Assert.Same(resolved, catalog.Resolved[1200]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvedAnimation_CrossSheetFrames_PreservesSourceOrder()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (500, new[] { 1000, 2000, 1001 }) });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(data, 2,
                new[] { (2000, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            var catalog = AsperetaAnimationCatalog.Load(data, AnimationSourceFixture.AsperetaCompiledEncPath(root));

            var frames = catalog.Resolved[500].Frames;
            Assert.Equal(3, frames.Count);
            Assert.Equal((AsperetaSheets.SheetBase, 1000), (frames[0].Sheet, frames[0].Frame.Index));
            Assert.Equal((AsperetaSheets.SheetBase + 1, 2000), (frames[1].Sheet, frames[1].Frame.Index));
            Assert.Equal((AsperetaSheets.SheetBase, 1001), (frames[2].Sheet, frames[2].Frame.Index));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnresolvedCompiledSlot_DiagnosticNamesTypeResourceMotionStateFacingAndReference()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            Directory.CreateDirectory(data);
            var indexes = new int[32];
            indexes[16 + 2 * 4 + 2] = 99999;
            var enc = AnimationSourceFixture.AsperetaCompiledEncPath(root);
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc, (AnimationType.Body, 200, indexes));

            var catalog = AsperetaAnimationCatalog.Load(data, enc);

            var slot = Assert.Single(catalog.CompiledSlots);
            Assert.Null(slot.Resolution);
            var diagnostic = Assert.Single(catalog.Diagnostics);
            Assert.Contains("Body", diagnostic);
            Assert.Contains("200", diagnostic);
            Assert.Contains("Attack", diagnostic);
            Assert.Contains("facing 2", diagnostic);
            Assert.Contains("state 3", diagnostic);
            Assert.Contains("99999", diagnostic);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnresolvedStandaloneDefinition_ProducesDiagnostic()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (700, new[] { 424242, 424243 }) });
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            var catalog = AsperetaAnimationCatalog.Load(data, AnimationSourceFixture.AsperetaCompiledEncPath(root));

            Assert.False(catalog.Resolved.ContainsKey(700));
            var diagnostic = Assert.Single(catalog.Diagnostics);
            Assert.Contains("700", diagnostic);
            Assert.Contains("424242", diagnostic);
            Assert.Empty(catalog.UnclaimedResolvedIds);
            Assert.Empty(catalog.ClaimedIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DuplicateFrameOwnership_ThrowsAndNamesBothSourceFiles()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (100, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaAdf(data, 2,
                new[] { (100, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            var ex = Assert.Throws<InvalidOperationException>(
                () => AsperetaAnimationCatalog.Load(data, AnimationSourceFixture.AsperetaCompiledEncPath(root)));
            Assert.Contains("1.adf", ex.Message);
            Assert.Contains("2.adf", ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DuplicateAnimationOwnership_ThrowsAndNamesBothSourceFiles()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (500, new[] { 1000, 1001 }) });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48) },
                new[] { (500, new[] { 1000, 1001 }) });
            AnimationSourceFixture.WriteAsperetaCompiledEnc(AnimationSourceFixture.AsperetaCompiledEncPath(root));

            var ex = Assert.Throws<InvalidOperationException>(
                () => AsperetaAnimationCatalog.Load(data, AnimationSourceFixture.AsperetaCompiledEncPath(root)));
            Assert.Contains("0.adf", ex.Message);
            Assert.Contains("1.adf", ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AllNonzeroCompiledIds_AreClaimed_EvenWhenUnresolved()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1200, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
            var indexes = new int[32];
            indexes[0] = 1200;
            indexes[1] = 99999;
            var enc = AnimationSourceFixture.AsperetaCompiledEncPath(root);
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc, (AnimationType.Body, 200, indexes));

            var catalog = AsperetaAnimationCatalog.Load(data, enc);

            Assert.Equal(new[] { 1200, 99999 }, catalog.ClaimedIds.OrderBy(id => id).ToArray());
            Assert.False(catalog.Resolved.ContainsKey(99999));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnclaimedResolvedIds_ListsResolvedDefinitionsNotClaimedByCompiled()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (500, new[] { 1000, 1001 }), (700, new[] { 1002, 1003 }) });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48), (1002, 48, 0, 24, 48), (1003, 72, 0, 24, 48) },
                Array.Empty<(int, int[])>());
            var indexes = new int[32];
            indexes[0] = 500;
            var enc = AnimationSourceFixture.AsperetaCompiledEncPath(root);
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc, (AnimationType.Body, 200, indexes));

            var catalog = AsperetaAnimationCatalog.Load(data, enc);

            Assert.Equal(new[] { 700 }, catalog.UnclaimedResolvedIds);
            Assert.True(catalog.Resolved.ContainsKey(500));
            Assert.Equal(new[] { 500 }, catalog.ClaimedIds.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvedDefinition_TakesPrecedenceOverDirectFrameFallback()
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                Array.Empty<(int, int, int, int, int)>(),
                new[] { (1200, new[] { 1200, 1201 }) });
            AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                new[] { (1200, 0, 0, 24, 48), (1201, 24, 0, 24, 48) }, Array.Empty<(int, int[])>());
            var indexes = new int[32];
            indexes[0] = 1200;
            var enc = AnimationSourceFixture.AsperetaCompiledEncPath(root);
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc, (AnimationType.Body, 200, indexes));

            var catalog = AsperetaAnimationCatalog.Load(data, enc);

            var resolved = catalog.Resolved[1200];
            Assert.False(resolved.DirectFrame);
            Assert.Equal(2, resolved.Frames.Count);
            Assert.Equal(1200, resolved.SourceId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReversedFixtureCreation_ProducesEquivalentOrderedProjections()
    {
        var forward = Project(LoadFixture(new[] { 0, 1, 2, 3 }));
        var reversed = Project(LoadFixture(new[] { 3, 2, 1, 0 }));

        Assert.Equal(forward.Resolved, reversed.Resolved);
        Assert.Equal(forward.Slots, reversed.Slots);
        Assert.Equal(forward.Claimed, reversed.Claimed);
        Assert.Equal(forward.Unclaimed, reversed.Unclaimed);
        Assert.Equal(forward.Diagnostics, reversed.Diagnostics);
        Assert.Equal(forward.Sheets, reversed.Sheets);
    }

    private static AsperetaAnimationCatalog LoadFixture(int[] fileOrder)
    {
        var root = AnimationSourceFixture.CreateDirectory();
        try
        {
            var data = AnimationSourceFixture.AsperetaDataDir(root);
            foreach (var file in fileOrder)
            {
                switch (file)
                {
                    case 0:
                        AnimationSourceFixture.WriteAsperetaAdf(data, 0,
                            Array.Empty<(int, int, int, int, int)>(),
                            new[] { (500, new[] { 1000, 2000 }, 3), (700, new[] { 1001, 1002 }, 5) });
                        break;
                    case 1:
                        AnimationSourceFixture.WriteAsperetaAdf(data, 1,
                            new[] { (1000, 0, 0, 24, 48), (1001, 24, 0, 24, 48), (1002, 48, 0, 24, 48) },
                            Array.Empty<(int, int[])>());
                        break;
                    case 2:
                        AnimationSourceFixture.WriteAsperetaAdf(data, 2,
                            new[] { (2000, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
                        break;
                    default:
                        AnimationSourceFixture.WriteAsperetaAdf(data, 3,
                            new[] { (1200, 0, 0, 24, 48) }, Array.Empty<(int, int[])>());
                        break;
                }
            }
            var indexes = new int[32];
            indexes[0] = 500;
            indexes[16] = 1200;
            var enc = AnimationSourceFixture.AsperetaCompiledEncPath(root);
            AnimationSourceFixture.WriteAsperetaCompiledEnc(enc, (AnimationType.Body, 200, indexes));
            return AsperetaAnimationCatalog.Load(data, enc);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (
        List<string> Resolved,
        List<string> Slots,
        List<int> Claimed,
        List<int> Unclaimed,
        List<string> Diagnostics,
        List<string> Sheets)
        Project(AsperetaAnimationCatalog catalog)
    {
        return (
            catalog.Resolved.Select(a =>
                $"{a.Key}|{a.Value.Fps}|{a.Value.DirectFrame}|"
                + string.Join(",", a.Value.Frames.Select(f => $"{f.Sheet}:{f.Frame.Index}"))).ToList(),
            catalog.CompiledSlots.Select(s =>
                $"{s.Type}|{s.ResourceId}|{s.Motion}|{s.State}|{s.Facing}|{s.ReferenceId}|{(s.Resolution?.SourceId ?? -1)}").ToList(),
            catalog.ClaimedIds.OrderBy(id => id).ToList(),
            catalog.UnclaimedResolvedIds.ToList(),
            catalog.Diagnostics.ToList(),
            catalog.Sheets.Select(s => $"{s.Key}->{s.Value.NewSheetNumber}").OrderBy(s => s).ToList());
    }
}
