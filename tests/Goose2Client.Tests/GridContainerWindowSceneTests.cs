using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class GridContainerWindowSceneTests
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
        var path = Path.Combine(RepositoryRoot(), "Scenes/UI/GridContainerWindow.tscn");
        Assert.True(File.Exists(path), $"{path} not found");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Scene_ReferencesGridContainerWindowScript()
    {
        Assert.Contains("res://Scripts/UI/GridContainerWindow.cs", Scene());
    }

    [Fact]
    public void Scene_HasNoSlotGrid()
    {
        Assert.DoesNotContain("SlotGrid", Scene());
    }

    [Fact]
    public void Scene_HasBackNextAndCloseButtonsUnderContent()
    {
        Assert.Contains("[node name=\"BackButton\" type=\"Button\" parent=\"Content\"]", Scene());
        Assert.Contains("[node name=\"NextButton\" type=\"Button\" parent=\"Content\"]", Scene());
        Assert.Contains("[node name=\"CloseButton\" type=\"Button\" parent=\"Content\"]", Scene());
    }

    [Fact]
    public void Scene_HasCloseButtonUnderTitleBar()
    {
        Assert.Contains("[node name=\"CloseButton\" type=\"Button\" parent=\"TitleBar\"]", Scene());
    }

    [Fact]
    public void Scene_RootWindowName_IsGridContainer()
    {
        Assert.Contains("WindowName = \"GridContainer\"", Scene());
    }
}
