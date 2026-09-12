using Goose2.AssetConverter.Manifest;
using Xunit;

namespace AssetConverter.Tests;

public class ManifestFileStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("manifest-store").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Write_PutsSidecarBesideArbitraryFrameManifestPath()
    {
        string framePath = Path.Combine(_root, "nested", "custom-name.json");

        ManifestFileStore.Write(framePath, () => "FRAME", () => "ANIM");

        Assert.Equal("FRAME", File.ReadAllText(framePath));
        Assert.Equal("ANIM", File.ReadAllText(Path.Combine(Path.GetDirectoryName(framePath)!, ManifestFileStore.AnimationFileName)));
    }

    [Fact]
    public void WriteCombined_PutsBothFilesUnderAssetsSprites()
    {
        ManifestFileStore.WriteCombined(_root, () => "FRAME", () => "ANIM");

        string dir = Path.Combine(_root, "Assets", "Sprites");
        Assert.Equal("FRAME", File.ReadAllText(Path.Combine(dir, "manifest.json")));
        Assert.Equal("ANIM", File.ReadAllText(Path.Combine(dir, ManifestFileStore.AnimationFileName)));
    }

    [Fact]
    public void Write_ThrowingSidecarFactoryLeavesPreexistingFilesUnchanged()
    {
        string dir = Path.Combine(_root, "existing");
        Directory.CreateDirectory(dir);
        string framePath = Path.Combine(dir, "manifest.json");
        string animPath = Path.Combine(dir, ManifestFileStore.AnimationFileName);
        File.WriteAllText(framePath, "OLD FRAME");
        File.WriteAllText(animPath, "OLD ANIM");

        Assert.Throws<InvalidOperationException>(() =>
            ManifestFileStore.Write(framePath, () => "NEW FRAME", () => throw new InvalidOperationException("boom")));

        Assert.Equal("OLD FRAME", File.ReadAllText(framePath));
        Assert.Equal("OLD ANIM", File.ReadAllText(animPath));
    }

    [Fact]
    public void Write_RepeatedWritesAreByteIdentical()
    {
        string framePath = Path.Combine(_root, "out", "manifest.json");

        ManifestFileStore.Write(framePath, () => "FRAME", () => "ANIM");
        byte[] frame1 = File.ReadAllBytes(framePath);
        byte[] anim1 = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(framePath)!, ManifestFileStore.AnimationFileName));

        ManifestFileStore.Write(framePath, () => "FRAME", () => "ANIM");
        Assert.Equal(frame1, File.ReadAllBytes(framePath));
        Assert.Equal(anim1, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(framePath)!, ManifestFileStore.AnimationFileName)));
    }
}
