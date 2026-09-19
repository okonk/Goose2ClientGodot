using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class ThemeResourceTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(RepositoryRoot(), path));

    [Fact]
    public void LoginScene_UsesBackgroundArtworkAndFocusedLoginCard()
    {
        var scene = Read("Scenes/Login.tscn");

        Assert.Contains("res://Assets/UI/goose-login.png", scene);
        Assert.Contains("node name=\"Background\" type=\"TextureRect\"", scene);
        Assert.Contains("node name=\"LoginCard\" type=\"PanelContainer\"", scene);
        Assert.Contains("node name=\"GameTitle\" type=\"Label\"", scene);
        Assert.DoesNotContain("LiberationSans-Bold.ttf", scene);
        Assert.Contains("text = \"CHARACTER NAME\"", scene);
        Assert.Contains("placeholder_text = \"Character name\"", scene);
        Assert.Contains("placeholder_text = \"Password\"", scene);
    }

    [Fact]
    public void DefaultFont_KeepsClearRenderingSettings()
    {
        var fontImport = Read("Assets/UI/Fonts/LiberationSans.ttf.import");

        Assert.Contains("antialiasing=1", fontImport);
        Assert.Contains("subpixel_positioning=4", fontImport);
    }

    [Fact]
    public void GameTheme_DefinesReadableInteractiveStates()
    {
        var theme = Read("Assets/UI/GameTheme.tres");

        Assert.Contains("Button/styles/hover", theme);
        Assert.Contains("Button/styles/focus", theme);
        Assert.Contains("LineEdit/styles/focus", theme);
        Assert.Contains("CheckBox/icons/checked", theme);
        Assert.Contains("HSlider/styles/slider", theme);
        Assert.Contains("TooltipPanel/styles/panel", theme);
    }

    [Fact]
    public void GameTheme_UsesClearLabelsAndLighterSlotPanels()
    {
        var theme = Read("Assets/UI/GameTheme.tres");

        Assert.DoesNotContain("\nLabel/colors/font_shadow_color", theme);
        Assert.Contains("SlotPanel/styles/panel", theme);

        foreach (var scenePath in new[]
                 {
                     "Scenes/UI/ItemSlot.tscn",
                     "Scenes/UI/HotbarSlot.tscn",
                     "Scenes/UI/SpellSlot.tscn"
                 })
            Assert.Contains("theme_type_variation = &\"SlotPanel\"", Read(scenePath));
    }

    [Fact]
    public void ChatLog_LeavesTheWindowBackgroundVisible()
    {
        var scene = Read("Scenes/UI/ChatWindow.tscn");

        Assert.Contains("sub_resource type=\"StyleBoxEmpty\" id=\"chat_log_transparent\"", scene);
        Assert.Contains("theme_override_styles/normal = SubResource(\"chat_log_transparent\")", scene);
    }

    [Fact]
    public void LoadingScene_UsesThemedTravelPanel()
    {
        var scene = Read("Scenes/LoadingMap.tscn");

        Assert.Contains("node name=\"Backdrop\" type=\"ColorRect\"", scene);
        Assert.Contains("node name=\"LoadingPanel\" type=\"PanelContainer\"", scene);
        Assert.Contains("text = \"TRAVELLING\"", scene);
    }
}
