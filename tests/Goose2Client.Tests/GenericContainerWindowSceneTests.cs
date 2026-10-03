using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class GenericContainerWindowSceneTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Scene()
    {
        var path = Path.Combine(RepositoryRoot(), "Scenes/UI/GenericContainerWindow.tscn");
        Assert.True(File.Exists(path), $"{path} not found");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Scene_ReferencesGenericContainerWindowScript()
    {
        Assert.Contains("res://Scripts/UI/GenericContainerWindow.cs", Scene());
    }

    [Fact]
    public void Scene_HasSlotGridUnderContent()
    {
        Assert.Contains("[node name=\"SlotGrid\" type=\"GridContainer\" parent=\"Content\"]", Scene());
    }

    [Fact]
    public void Scene_HasBackAndNextButtonsUnderContent()
    {
        Assert.Contains("[node name=\"BackButton\" type=\"Button\" parent=\"Content\"]", Scene());
        Assert.Contains("[node name=\"NextButton\" type=\"Button\" parent=\"Content\"]", Scene());
    }

    [Fact]
    public void Scene_HasCloseButtonUnderTitleBar()
    {
        Assert.Contains("[node name=\"CloseButton\" type=\"Button\" parent=\"TitleBar\"]", Scene());
    }

    [Fact]
    public void Scene_RootWindowName_IsGenericContainer()
    {
        Assert.Contains("WindowName = \"GenericContainer\"", Scene());
    }
}
