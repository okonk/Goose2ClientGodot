using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class AmountWindowSceneTests
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
        var path = Path.Combine(RepositoryRoot(), "Scenes/UI/AmountWindow.tscn");
        Assert.True(File.Exists(path), $"{path} not found");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Scene_ReferencesAmountWindowScript()
    {
        Assert.Contains("res://Scripts/UI/AmountWindow.cs", Scene());
    }

    [Fact]
    public void Scene_HasTitleBarWithLabelAndCloseButton()
    {
        Assert.Contains("[node name=\"TitleBar\" type=\"Control\" parent=\".\"]", Scene());
        Assert.Contains("[node name=\"TitleLabel\" type=\"Label\" parent=\"TitleBar\"]", Scene());
        Assert.Contains("[node name=\"CloseButton\" type=\"Button\" parent=\"TitleBar\"]", Scene());
    }

    [Fact]
    public void Scene_HasContentWithIconPromptAndField()
    {
        Assert.Contains("[node name=\"Content\" type=\"Control\" parent=\".\"]", Scene());
        Assert.Contains("[node name=\"Icon\" type=\"TextureRect\" parent=\"Content\"]", Scene());
        Assert.Contains("[node name=\"Prompt\" type=\"Label\" parent=\"Content\"]", Scene());
        Assert.Contains("[node name=\"AmountField\" type=\"LineEdit\" parent=\"Content\"]", Scene());
    }

    [Fact]
    public void Scene_HasMaxDonateAndCancelButtonUnderContent()
    {
        Assert.Contains("[node name=\"MaxButton\" type=\"Button\" parent=\"Content\"]", Scene());
        Assert.Contains("[node name=\"DonateButton\" type=\"Button\" parent=\"Content\"]", Scene());
        Assert.Contains("[node name=\"CancelButton\" type=\"Button\" parent=\"Content\"]", Scene());
    }

    [Fact]
    public void Scene_RootWindowName_IsAmount()
    {
        Assert.Contains("WindowName = \"Amount\"", Scene());
    }
}
