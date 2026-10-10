using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class TargetColorPickerSceneTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    private static string NodeBlock(string scene, string name)
    {
        int start = scene.IndexOf($"[node name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"node '{name}' not found");
        int end = scene.IndexOf("[node name=", start + 1, StringComparison.Ordinal);
        return scene.Substring(start, end < 0 ? scene.Length - start : end - start);
    }

    private static string OptionsScene() => Read("Scenes/UI/OptionsWindow.tscn");

    [Fact]
    public void Options_ShowsSwatchBeforeTargetBoxLabel()
    {
        var s = OptionsScene();
        var swatch = NodeBlock(s, "TargetColorSwatch");
        Assert.Contains("parent=\"Content\"", swatch);
        Assert.Contains("type=\"Panel\"", swatch);
        Assert.Contains("offset_left = 8.0", swatch);
        Assert.Contains("offset_top = 284.0", swatch);
        Assert.Contains("offset_right = 32.0", swatch);
        Assert.Contains("offset_bottom = 308.0", swatch);
        Assert.Contains("mouse_default_cursor_shape = 2", swatch);

        var label = NodeBlock(s, "TargetColorLabel");
        Assert.Contains("text = \"Target Box Color\"", label);
        Assert.Contains("offset_left = 40.0", label);
        Assert.Contains("offset_top = 284.0", label);
    }

    [Fact]
    public void Options_ColorRowSitsAboveTheButtonRow()
    {
        var s = OptionsScene();
        Assert.Contains("offset_top = 284.0", NodeBlock(s, "TargetColorSwatch"));
        Assert.Contains("offset_top = 340.0", NodeBlock(s, "ResetLayoutButton"));
    }

    [Fact]
    public void Options_HiddenPickerHoldsTheSharedControlWithoutAlpha()
    {
        var s = OptionsScene();
        var panel = NodeBlock(s, "TargetColorPicker");
        Assert.Contains("visible = false", panel);
        Assert.Contains("parent=\"Content\"", panel);

        var picker = NodeBlock(s, "Picker");
        Assert.Contains("parent=\"Content/TargetColorPicker\"", picker);
        Assert.Contains("instance=ExtResource(\"2_picker\")", picker);
        Assert.Contains("ShowAlpha = false", picker);
        Assert.Contains("res://Scenes/UI/ColorPickerControl.tscn", s);

        // Tree order is draw order: the panel must come after the window's own controls.
        Assert.True(
            s.IndexOf("[node name=\"TargetColorPicker\"", StringComparison.Ordinal) >
            s.IndexOf("[node name=\"KeyBindingsButton\"", StringComparison.Ordinal),
            "picker panel must be declared after the window controls it overlaps");
    }

    [Fact]
    public void PickerControl_DeclaresEveryChannelAndBar()
    {
        var s = Read("Scenes/UI/ColorPickerControl.tscn");
        foreach (string node in new[]
                 {
                     "RSlider", "GSlider", "BSlider", "ASlider", "RValue", "GValue", "BValue", "AValue", "ALabel"
                 })
            Assert.Contains($"[node name=\"{node}\"", s);

        foreach (string bar in new[] { "Swatch", "HueBar", "LightBar" })
        {
            Assert.Contains($"[node name=\"{bar}\" type=\"TextureRect\" parent=\".\"]", s);
            Assert.Contains($"[node name=\"Cursor\" type=\"TextureRect\" parent=\"{bar}\"]", s);
        }

        Assert.Contains("res://Scripts/UI/ColorPickerControl.cs", s);
    }

    [Fact]
    public void CustomWindow_UsesTheSharedPickerInPlaceOfItsOwnNodes()
    {
        var s = Read("Scenes/UI/CustomWindow.tscn");
        var picker = NodeBlock(s, "ColorPicker");
        Assert.Contains("parent=\"Content\"", picker);
        Assert.Contains("instance=ExtResource(\"4_cpc\")", picker);
        Assert.Contains("offset_left = 12.0", picker);
        Assert.Contains("offset_top = 30.0", picker);
        Assert.DoesNotContain("[node name=\"Swatch\" type=\"TextureRect\" parent=\"Content\"]", s);
        Assert.DoesNotContain("[node name=\"RSlider\" type=\"HSlider\" parent=\"Content\"]", s);
    }
}
