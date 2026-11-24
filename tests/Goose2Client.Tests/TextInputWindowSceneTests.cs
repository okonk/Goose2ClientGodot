using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class TextInputWindowSceneTests
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
        var path = Path.Combine(RepositoryRoot(), "Scenes/UI/TextInputWindow.tscn");
        Assert.True(File.Exists(path), $"{path} not found");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Scene_ReferencesTextInputWindowScript()
    {
        Assert.Contains("res://Scripts/UI/TextInputWindow.cs", Scene());
    }

    [Fact]
    public void Scene_HasTitleBarWithLabelAndCloseButton()
    {
        Assert.Contains("[node name=\"TitleBar\" type=\"Control\" parent=\".\"]", Scene());
        Assert.Contains("[node name=\"TitleLabel\" type=\"Label\" parent=\"TitleBar\"]", Scene());
        Assert.Contains("[node name=\"CloseButton\" type=\"Button\" parent=\"TitleBar\"]", Scene());
    }

    [Fact]
    public void Scene_HasContentWithPromptAndField()
    {
        Assert.Contains("[node name=\"Content\" type=\"Control\" parent=\".\"]", Scene());
        Assert.Contains("[node name=\"Prompt\" type=\"Label\" parent=\"Content\"]", Scene());
        Assert.Contains("[node name=\"TextField\" type=\"LineEdit\" parent=\"Content\"]", Scene());
    }

    [Fact]
    public void Scene_HasConfirmAndCancelButtonUnderContent()
    {
        Assert.Contains("[node name=\"ConfirmButton\" type=\"Button\" parent=\"Content\"]", Scene());
        Assert.Contains("[node name=\"CancelButton\" type=\"Button\" parent=\"Content\"]", Scene());
    }

    [Fact]
    public void Scene_RootWindowName_IsTextInput()
    {
        Assert.Contains("WindowName = \"TextInput\"", Scene());
    }
}
