using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class KeyBindingsSceneTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    private static string KeyBindingsScene() => Read("Scenes/UI/KeyBindingsWindow.tscn");

    private static string OptionsScene() => Read("Scenes/UI/OptionsWindow.tscn");

    private static string NodeBlock(string scene, string name)
    {
        int start = scene.IndexOf($"[node name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"node '{name}' not found");
        int end = scene.IndexOf("[node name=", start + 1, StringComparison.Ordinal);
        return scene.Substring(start, end < 0 ? scene.Length - start : end - start);
    }

    [Fact]
    public void Root_UsesStandardChromeAndDesignSize()
    {
        var s = KeyBindingsScene();
        Assert.Contains("[node name=\"KeyBindingsWindow\" type=\"Control\"]", s);
        Assert.Contains("WindowName = \"KeyBindings\"", s);
        Assert.Contains("offset_right = 760.0", s);
        Assert.Contains("offset_bottom = 520.0", s);
        Assert.Contains("node name=\"Background\" type=\"Panel\"", s);
        Assert.Contains("node name=\"TitleBar\" type=\"Control\"", s);
        Assert.Contains("node name=\"TitleLabel\" type=\"Label\"", s);
        Assert.Contains("node name=\"CloseButton\" type=\"Button\"", s);
        Assert.Contains("node name=\"Content\" type=\"Control\"", s);
    }

    [Fact]
    public void EditorControls_ArePresent()
    {
        var s = KeyBindingsScene();
        Assert.Contains("[node name=\"SearchField\" type=\"LineEdit\" parent=\"Content/RootBox/SearchRow\"]", s);
        Assert.Contains("node name=\"ScrollHost\" type=\"ScrollContainer\"", s);
        Assert.Contains("node name=\"RowsBox\" type=\"VBoxContainer\"", s);
        Assert.Contains("node name=\"StatusLabel\" type=\"Label\"", s);
        Assert.Contains("node name=\"ResetAllButton\" type=\"Button\"", s);
        Assert.Contains("text = \"Reset All\"", s);
        Assert.Contains("node name=\"ApplyButton\" type=\"Button\"", s);
        Assert.Contains("text = \"Apply\"", s);
        Assert.Contains("node name=\"CancelButton\" type=\"Button\"", s);
        Assert.Contains("text = \"Cancel\"", s);
    }

    [Fact]
    public void CaptureOverlay_IsHiddenInitiallyWithPromptAndExplicitCancel()
    {
        var s = KeyBindingsScene();
        Assert.Contains("visible = false", NodeBlock(s, "CaptureOverlay"));
        Assert.Contains("node name=\"CapturePrompt\" type=\"Label\"", s);
        Assert.Contains("node name=\"CancelCaptureButton\" type=\"Button\"", s);
        Assert.Contains("text = \"Cancel Capture\"", s);
    }

    [Fact]
    public void Layout_IsAnchoredOrContainerManagedNotFixedOffsets()
    {
        var s = KeyBindingsScene();
        Assert.DoesNotContain("anchors_preset = 0", s);
        Assert.Contains("size_flags_vertical = 3", s);
        Assert.Contains("size_flags_horizontal = 3", s);
        Assert.Contains("ScrollContainer", s);
        Assert.Contains("VBoxContainer", s);
        Assert.Contains("HBoxContainer", s);
    }

    [Fact]
    public void Options_GrowsTo344KeepsResetLayoutAndAddsKeyBindingsButton()
    {
        var s = OptionsScene();
        var root = NodeBlock(s, "OptionsWindow");
        Assert.Contains("offset_bottom = 344.0", root);

        var reset = NodeBlock(s, "ResetLayoutButton");
        Assert.Contains("offset_top = 284.0", reset);
        Assert.Contains("offset_bottom = 308.0", reset);
        Assert.Contains("text = \"Reset UI Layout\"", reset);

        var button = NodeBlock(s, "KeyBindingsButton");
        Assert.Contains("parent=\"Content\"", button);
        Assert.Contains("offset_top = 312.0", button);
        Assert.Contains("offset_bottom = 336.0", button);
        Assert.Contains("text = \"Key Bindings…\"", button);
        Assert.True(
            s.IndexOf("[node name=\"KeyBindingsButton\"", StringComparison.Ordinal) >
            s.IndexOf("[node name=\"ResetLayoutButton\"", StringComparison.Ordinal),
            "Key Bindings button must come after Reset UI Layout");
    }

    [Fact]
    public void Options_LegacySizeRemains240x112()
        => Assert.Equal(new Vector2(240, 112), DefaultWindowLayout.LegacySize("Options"));

    [Theory]
    [InlineData("Scenes/UI/KeyBindingsWindow.tscn")]
    [InlineData("Scenes/UI/OptionsWindow.tscn")]
    public void EveryParentPath_ResolvesToAPreviouslyDeclaredNode(string scenePath)
    {
        var declared = new HashSet<string> { "." };
        var lines = Read(scenePath).Split('\n');
        int nodes = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (!line.StartsWith("[node name=\""))
                continue;
            nodes++;
            int nameEnd = line.IndexOf("\" type=", StringComparison.Ordinal);
            string name = line.Substring(12, nameEnd - 12);
            int parentStart = line.IndexOf("parent=\"", StringComparison.Ordinal);
            string parent = parentStart < 0 ? "." : line.Substring(parentStart + 8, line.IndexOf('"', parentStart + 8) - parentStart - 8);
            Assert.True(declared.Contains(parent), $"{scenePath} line {i + 1}: parent '{parent}' not declared");
            declared.Add(parent == "." ? name : parent + "/" + name);
        }
        Assert.True(nodes >= 5);
    }
}
