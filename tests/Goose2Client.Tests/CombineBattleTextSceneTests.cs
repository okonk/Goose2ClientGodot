using System;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class CombineBattleTextSceneTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    private static string OptionsScene() => Read("Scenes/UI/OptionsWindow.tscn");

    private static string NodeBlock(string scene, string name)
    {
        int start = scene.IndexOf($"[node name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"node '{name}' not found");
        int end = scene.IndexOf("[node name=", start + 1, StringComparison.Ordinal);
        return scene.Substring(start, end < 0 ? scene.Length - start : end - start);
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"signature not found: {signature}");
        int brace = source.IndexOf('{', start);
        Assert.True(brace > start, $"no brace after signature: {signature}");
        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException($"unbalanced braces after: {signature}");
    }

    [Fact]
    public void Scene_HasCombineCheck_BelowNameDisplay_AboveButtons()
    {
        var s = OptionsScene();
        var check = NodeBlock(s, "CombineBattleTextCheck");
        Assert.Contains("type=\"CheckBox\"", check);
        Assert.Contains("parent=\"Content\"", check);
        Assert.Contains("offset_top = 340.0", check);
        Assert.Contains("offset_bottom = 364.0", check);
        Assert.Contains("text = \"Combine damage & heal numbers\"", check);
    }

    [Fact]
    public void Source_ReadsWritesOptionAndLiveFlag()
    {
        var src = Read("Scripts/UI/OptionsWindow.cs");
        Assert.Contains("GetNode<CheckBox>(\"Content/CombineBattleTextCheck\")", src);
        Assert.Contains("GetOption<bool>(Options.CombineBattleText, true)", src);
        Assert.Contains("cs.Options[Options.CombineBattleText] = pressed", src);
        Assert.Contains("GameManager.Instance.CombineBattleText = pressed", src);

        var body = MethodBody(src, "private void OnCombineBattleTextChanged(bool pressed)");
        Assert.Contains("cs.Save();", body);
    }
}
