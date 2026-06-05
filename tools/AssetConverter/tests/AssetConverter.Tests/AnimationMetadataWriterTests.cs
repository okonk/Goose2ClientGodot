using Goose2.AssetConverter.SpriteFrames;
using Xunit;

namespace AssetConverter.Tests;

public class AnimationMetadataWriterTests
{
    [Fact]
    public void BuildFirstFrameText_WritesDeterministicUnityCompatibleLines()
    {
        var frames = new Dictionary<string, AnimationFrameInfo>
        {
            ["Hair-2"] = new(200, 3000, 32, 48),
            ["Body-1"] = new(115, 3205, 24, 48),
        };

        Assert.Equal("Body-1,115,3205,24,48\nHair-2,200,3000,32,48\n",
            AnimationMetadataWriter.BuildFirstFrameText(frames));
    }

    [Fact]
    public void BuildHeightsText_WritesDeterministicUnityCompatibleLines()
    {
        var heights = new Dictionary<string, int>
        {
            ["walk-left"] = 48,
            ["idle-left"] = 48,
        };

        Assert.Equal("idle-left,48\nwalk-left,48\n",
            AnimationMetadataWriter.BuildHeightsText(heights));
    }

    [Fact]
    public void MergeFirstFrames_ThrowsOnConflictingDuplicateKey()
    {
        var a = new Dictionary<string, AnimationFrameInfo> { ["Body-1"] = new(115, 3205, 24, 48) };
        var b = new Dictionary<string, AnimationFrameInfo> { ["Body-1"] = new(999, 3205, 24, 48) };

        Assert.Throws<InvalidOperationException>(() => AnimationMetadataWriter.MergeFirstFrames(new[] { a, b }));
    }

    [Fact]
    public void MergeFirstFrames_AllowsSameValueDuplicateKey()
    {
        var info = new AnimationFrameInfo(115, 3205, 24, 48);
        var a = new Dictionary<string, AnimationFrameInfo> { ["Body-1"] = info };
        var b = new Dictionary<string, AnimationFrameInfo> { ["Body-1"] = info };

        var merged = AnimationMetadataWriter.MergeFirstFrames(new[] { a, b });

        Assert.Single(merged);
        Assert.Equal(info, merged["Body-1"]);
    }

    [Fact]
    public void MergeHeights_ThrowsOnConflictingDuplicateKey()
    {
        var a = new Dictionary<string, int> { ["walk-left"] = 48 };
        var b = new Dictionary<string, int> { ["walk-left"] = 64 };

        Assert.Throws<InvalidOperationException>(() => AnimationMetadataWriter.MergeHeights(new[] { a, b }));
    }

    [Fact]
    public void MergeHeights_AllowsSameValueDuplicateKey()
    {
        var a = new Dictionary<string, int> { ["walk-left"] = 48 };
        var b = new Dictionary<string, int> { ["walk-left"] = 48 };

        var merged = AnimationMetadataWriter.MergeHeights(new[] { a, b });

        Assert.Single(merged);
        Assert.Equal(48, merged["walk-left"]);
    }

    [Fact]
    public void MergeFirstFrames_MergesMultipleDictionaries()
    {
        var a = new Dictionary<string, AnimationFrameInfo> { ["Body-1"] = new(115, 3205, 24, 48) };
        var b = new Dictionary<string, AnimationFrameInfo> { ["Hair-2"] = new(200, 3000, 32, 48) };

        var merged = AnimationMetadataWriter.MergeFirstFrames(new[] { a, b });

        Assert.Equal(2, merged.Count);
        Assert.Equal(new AnimationFrameInfo(115, 3205, 24, 48), merged["Body-1"]);
        Assert.Equal(new AnimationFrameInfo(200, 3000, 32, 48), merged["Hair-2"]);
    }

    [Fact]
    public void Write_CreatesDirectoryAndWritesBothFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"meta-{Guid.NewGuid()}");
        var frames = new Dictionary<string, AnimationFrameInfo>
        {
            ["Body-1"] = new(115, 3205, 24, 48),
        };
        var heights = new Dictionary<string, int>
        {
            ["walk-left"] = 48,
        };

        AnimationMetadataWriter.Write(dir, frames, heights);

        Assert.True(Directory.Exists(dir));
        Assert.Equal("Body-1,115,3205,24,48\n",
            File.ReadAllText(Path.Combine(dir, "AnimationToFirstFrame.txt")));
        Assert.Equal("walk-left,48\n",
            File.ReadAllText(Path.Combine(dir, "AnimationHeights.txt")));

        Directory.Delete(dir, true);
    }
}
