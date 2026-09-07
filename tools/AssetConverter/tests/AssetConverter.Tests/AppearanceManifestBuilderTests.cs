using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Manifest;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

namespace AssetConverter.Tests;

public class AppearanceManifestBuilderTests
{
    private const string LockedEmpty =
        "{\"version\":1,\"parts\":{\"Body\":{},\"Hair\":{},\"Eyes\":{},\"Chest\":{},\"Helm\":{},\"Legs\":{},\"Feet\":{},\"Hand\":{}}}";

    private static Frame Frame(int index) => new(index, 0, 0, 48, 64);

    private static SpriteFramesAnimationSpec Spec(string name, int sheet, params int[] frameIndexes) =>
        SpriteFramesAnimationSpec.FromFrames(
            name, sheet, "res://Assets/Sprites/sheets/1.png", frameIndexes.Select(Frame).ToArray());

    private static CompiledSpriteFramesResource Resource(
        AnimationType type, int id, params SpriteFramesAnimationSpec[] animations) =>
        new(type, id, AnimationNaming.ResourceRelativePath(type, id), animations,
            new Dictionary<string, AnimationFrameInfo>(), new Dictionary<string, int>(),
            Array.Empty<string>());

    [Fact]
    public void Build_NoResources_EmitsLockedV1Shape()
    {
        string json = AppearanceManifestBuilder.Build(Array.Empty<CompiledSpriteFramesResource>());
        Assert.Equal(LockedEmpty, json);
    }

    [Fact]
    public void Build_SingleBody_EmitsLockedExample()
    {
        var body = Resource(AnimationType.Body, 1,
            Spec("idle-no-equip-down", 1000, 108760),
            Spec("idle-equip-down", 1001, 108900));

        string json = AppearanceManifestBuilder.Build(new[] { body });

        Assert.Equal(
            "{\"version\":1,\"parts\":{\"Body\":{\"1\":{\"noEquip\":[1000,108760],\"equip\":[1001,108900]}},\"Hair\":{},\"Eyes\":{},\"Chest\":{},\"Helm\":{},\"Legs\":{},\"Feet\":{},\"Hand\":{}}}",
            json);
    }

    [Fact]
    public void Build_MixedUnsortedResources_SortsKindsAndNumericIds()
    {
        var resources = new[]
        {
            Resource(AnimationType.Hand, 10, Spec("idle-no-equip-down", 3, 30)),
            Resource(AnimationType.Body, 10, Spec("idle-equip-down", 4, 40)),
            Resource(AnimationType.Feet, 1, Spec("idle-no-equip-down", 5, 50)),
            Resource(AnimationType.Body, 2, Spec("idle-no-equip-down", 6, 60)),
            Resource(AnimationType.Hair, 3, Spec("idle-equip-down", 7, 70)),
        };

        string json = AppearanceManifestBuilder.Build(resources);

        Assert.Equal(
            "{\"version\":1,\"parts\":{\"Body\":{\"2\":{\"noEquip\":[6,60]},\"10\":{\"equip\":[4,40]}},\"Hair\":{\"3\":{\"equip\":[7,70]}},\"Eyes\":{},\"Chest\":{},\"Helm\":{},\"Legs\":{},\"Feet\":{\"1\":{\"noEquip\":[5,50]}},\"Hand\":{\"10\":{\"noEquip\":[3,30]}}}}",
            json);
    }

    [Fact]
    public void Build_MultiFrameDownIdle_UsesFirstFrame()
    {
        var body = Resource(AnimationType.Body, 1,
            Spec("idle-no-equip-down", 5, 11, 12, 13));

        string json = AppearanceManifestBuilder.Build(new[] { body });

        Assert.Contains("\"noEquip\":[5,11]", json);
    }

    [Fact]
    public void Build_NonDownAndAttackClips_AreIgnored()
    {
        var body = Resource(AnimationType.Body, 1,
            Spec("idle-no-equip-up", 1, 10),
            Spec("idle-no-equip-left", 2, 20),
            Spec("idle-no-equip-right", 3, 30),
            Spec("idle-equip-up", 4, 40),
            Spec("attack-no-equip-down", 5, 50),
            Spec("walk-no-equip-down", 6, 60));

        string json = AppearanceManifestBuilder.Build(new[] { body });

        Assert.Equal(LockedEmpty, json);
    }

    [Fact]
    public void Build_NoEquipOnly_OmitsEquipProperty()
    {
        var body = Resource(AnimationType.Body, 1, Spec("idle-no-equip-down", 1, 10));
        string json = AppearanceManifestBuilder.Build(new[] { body });
        Assert.Contains("\"Body\":{\"1\":{\"noEquip\":[1,10]}}", json);
        Assert.DoesNotContain("equip", json);
    }

    [Fact]
    public void Build_EquipOnly_OmitsNoEquipProperty()
    {
        var body = Resource(AnimationType.Body, 1, Spec("idle-equip-down", 2, 20));
        string json = AppearanceManifestBuilder.Build(new[] { body });
        Assert.Contains("\"Body\":{\"1\":{\"equip\":[2,20]}}", json);
        Assert.DoesNotContain("noEquip", json);
    }

    [Fact]
    public void Build_BothVariantsSameReference_ShareFrame()
    {
        var body = Resource(AnimationType.Body, 1,
            Spec("idle-no-equip-down", 9, 90),
            Spec("idle-equip-down", 9, 90));

        string json = AppearanceManifestBuilder.Build(new[] { body });

        Assert.Contains("\"Body\":{\"1\":{\"noEquip\":[9,90],\"equip\":[9,90]}}", json);
    }

    [Fact]
    public void Build_AllEightKinds_MapExactlyWithShieldAndWeaponUnderHand()
    {
        var resources = new[]
        {
            Resource(AnimationType.Body, 1, Spec("idle-no-equip-down", 1, 10)),
            Resource(AnimationType.Hair, 1, Spec("idle-no-equip-down", 2, 20)),
            Resource(AnimationType.Eyes, 1, Spec("idle-no-equip-down", 3, 30)),
            Resource(AnimationType.Chest, 1, Spec("idle-no-equip-down", 4, 40)),
            Resource(AnimationType.Helm, 1, Spec("idle-no-equip-down", 5, 50)),
            Resource(AnimationType.Legs, 1, Spec("idle-no-equip-down", 6, 60)),
            Resource(AnimationType.Feet, 1, Spec("idle-no-equip-down", 7, 70)),
            Resource(AnimationType.Hand, 1, Spec("idle-no-equip-down", 8, 80)),
            Resource(AnimationType.Hand, 2, Spec("idle-no-equip-down", 9, 90)),
        };

        string json = AppearanceManifestBuilder.Build(resources);

        Assert.Equal(
            "{\"version\":1,\"parts\":{\"Body\":{\"1\":{\"noEquip\":[1,10]}},\"Hair\":{\"1\":{\"noEquip\":[2,20]}},\"Eyes\":{\"1\":{\"noEquip\":[3,30]}},\"Chest\":{\"1\":{\"noEquip\":[4,40]}},\"Helm\":{\"1\":{\"noEquip\":[5,50]}},\"Legs\":{\"1\":{\"noEquip\":[6,60]}},\"Feet\":{\"1\":{\"noEquip\":[7,70]}},\"Hand\":{\"1\":{\"noEquip\":[8,80]},\"2\":{\"noEquip\":[9,90]}}}}",
            json);
    }

    [Fact]
    public void Build_DuplicateEqualResources_Coalesce()
    {
        var a = Resource(AnimationType.Body, 1, Spec("idle-no-equip-down", 1, 10));
        var b = Resource(AnimationType.Body, 1, Spec("idle-no-equip-down", 1, 10));

        string json = AppearanceManifestBuilder.Build(new[] { a, b });

        Assert.Contains("\"Body\":{\"1\":{\"noEquip\":[1,10]}}", json);
        Assert.Equal(1, json.Split("\"Body\":{\"1\"").Length - 1);
    }

    [Fact]
    public void Build_DuplicateConflictingResources_Throws()
    {
        var a = Resource(AnimationType.Body, 1, Spec("idle-no-equip-down", 1, 10));
        var b = Resource(AnimationType.Body, 1, Spec("idle-no-equip-down", 1, 11));

        Assert.Throws<InvalidOperationException>(() => AppearanceManifestBuilder.Build(new[] { a, b }));
    }

    [Fact]
    public void Build_RepeatedBuilds_AreByteIdentical()
    {
        var resources = new[]
        {
            Resource(AnimationType.Hand, 10, Spec("idle-equip-down", 3, 30)),
            Resource(AnimationType.Body, 2, Spec("idle-no-equip-down", 6, 60)),
            Resource(AnimationType.Body, 10, Spec("idle-equip-down", 4, 40)),
        };

        Assert.Equal(
            AppearanceManifestBuilder.Build(resources),
            AppearanceManifestBuilder.Build(resources.Reverse().ToArray()));
    }

    [Fact]
    public void Write_CreatesSidecarAtLockedPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "ac_appearance_" + Guid.NewGuid().ToString("N"));
        try
        {
            AppearanceManifestFileStore.Write(root, LockedEmpty);

            string path = Path.Combine(root, AppearanceManifestFileStore.RelativePath);
            Assert.Equal(LockedEmpty, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Write_RepeatedWrites_AreByteIdentical()
    {
        string root = Path.Combine(Path.GetTempPath(), "ac_appearance_" + Guid.NewGuid().ToString("N"));
        try
        {
            AppearanceManifestFileStore.Write(root, LockedEmpty);
            string path = Path.Combine(root, AppearanceManifestFileStore.RelativePath);
            byte[] first = File.ReadAllBytes(path);

            AppearanceManifestFileStore.Write(root, LockedEmpty);

            Assert.Equal(first, File.ReadAllBytes(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Write_FailureBeforeOrDuringReplace_PreservesExistingSidecarAndLeavesNoTemp(
        bool failOnWrite, bool failOnReplace)
    {
        string root = Path.Combine(Path.GetTempPath(), "ac_appearance_" + Guid.NewGuid().ToString("N"));
        try
        {
            AppearanceManifestFileStore.Write(root, "PRIOR");
            string path = Path.Combine(root, AppearanceManifestFileStore.RelativePath);
            string dir = Path.GetDirectoryName(path)!;
            byte[] prior = File.ReadAllBytes(path);

            var ops = new RecordingFileOperations
            {
                FailOnWrite = failOnWrite,
                FailOnReplace = failOnReplace,
            };

            Assert.Throws<IOException>(() => AppearanceManifestFileStore.Write(root, "NEW", ops));

            Assert.Equal(prior, File.ReadAllBytes(path));
            Assert.Empty(Directory.EnumerateFiles(dir, "*.tmp-*"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Write_MoveFailureWithoutExistingSidecar_LeavesNoDestinationOrTemp()
    {
        string root = Path.Combine(Path.GetTempPath(), "ac_appearance_" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(root, AppearanceManifestFileStore.RelativePath);
            string dir = Path.GetDirectoryName(path)!;

            var ops = new RecordingFileOperations { FailOnMove = true };

            Assert.Throws<IOException>(() => AppearanceManifestFileStore.Write(root, "NEW", ops));

            Assert.False(File.Exists(path));
            Assert.Empty(Directory.EnumerateFiles(dir, "*.tmp-*"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingFileOperations : IAppearanceManifestFileOperations
    {
        public bool FailOnWrite { get; init; }
        public bool FailOnReplace { get; init; }
        public bool FailOnMove { get; init; }

        public bool Exists(string path) => File.Exists(path);

        public Stream CreateFile(string path) => new FailingStream(File.Create(path), FailOnWrite);

        public void Replace(string source, string destination)
        {
            if (FailOnReplace) throw new IOException("injected replace failure");
            File.Move(source, destination, overwrite: true);
        }

        public void Move(string source, string destination)
        {
            if (FailOnMove) throw new IOException("injected move failure");
            File.Move(source, destination);
        }

        public void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class FailingStream(Stream inner, bool fail) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            if (fail) throw new IOException("injected flush failure");
            inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (fail) throw new IOException("injected write failure");
            inner.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
