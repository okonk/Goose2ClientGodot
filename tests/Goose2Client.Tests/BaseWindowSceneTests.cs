using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class BaseWindowSceneTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    [Fact]
    public void CloseButton_IsAnchoredToTitleBarRightEdge()
    {
        var scene = File.ReadAllText(Path.Combine(RepositoryRoot(), "Scenes/UI/BaseWindow.tscn"));
        int start = scene.IndexOf("[node name=\"CloseButton\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "CloseButton node not found");
        int end = scene.IndexOf("[node name=", start + 1, StringComparison.Ordinal);
        var block = scene.Substring(start, end < 0 ? scene.Length - start : end - start);

        Assert.Contains("anchor_left = 1.0", block);
        Assert.Contains("anchor_right = 1.0", block);
        Assert.Contains("offset_bottom = -2.0", block);
        Assert.DoesNotContain("offset_bottom = 22.0", block);
    }
}
