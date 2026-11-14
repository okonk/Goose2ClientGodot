using System;
using System.IO;
using System.Linq;
using Goose2Client.UI;
using Xunit;

public class StylistCatalogTests
{
    [Fact]
    public void HairIds_AreExactlyOneToFortySix()
    {
        Assert.Equal(Enumerable.Range(1, 46), StylistCatalog.HairIds);
    }

    [Fact]
    public void FaceIds_AreExactlyOneToEighteen()
    {
        Assert.Equal(Enumerable.Range(1, 18), StylistCatalog.FaceIds);
    }

    [Fact]
    public void EveryCatalogId_HasAnimationAsset()
    {
        var root = FindRepoRoot();

        var missing = StylistCatalog.HairIds
            .Where(id => !File.Exists(Path.Combine(root, "Assets", "Sprites", "Hair", id.ToString(), "animations.tres")))
            .Select(id => $"Hair/{id}")
            .Concat(StylistCatalog.FaceIds
                .Where(id => !File.Exists(Path.Combine(root, "Assets", "Sprites", "Eyes", id.ToString(), "animations.tres")))
                .Select(id => $"Eyes/{id}"));

        Assert.Empty(missing);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
