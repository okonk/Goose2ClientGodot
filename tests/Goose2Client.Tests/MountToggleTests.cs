using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class MountToggleTests
{
    [Fact]
    public void MountButton_SendsMnt_WhenAMountIsEquipped()
    {
        string clientSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "Scripts/Network/NetworkClient.cs"));
        Assert.Contains("Send(\"MNT\")", clientSource);

        string hotbarSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "Scripts/UI/HotbarWindow.cs"));
        Assert.Contains("NetworkClient.ToggleMount()", hotbarSource);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory!.FullName;
    }
}
