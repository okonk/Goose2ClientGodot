using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Goose2Client.Tests;

public class InputBindingCompositionContractTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    private static IEnumerable<(string File, string Text)> ScriptFiles()
    {
        var scripts = new DirectoryInfo(Path.Combine(RepositoryRoot(), "Scripts"));
        foreach (var file in scripts.GetFiles("*.cs", SearchOption.AllDirectories))
            yield return (file.Name, File.ReadAllText(file.FullName));
    }

    [Fact]
    public void GameManager_OwnsExactlyOneService()
    {
        var instantiations = ScriptFiles()
            .Where(f => f.Text.Contains("new InputBindingService(", StringComparison.Ordinal))
            .Select(f => f.File)
            .ToList();

        Assert.Equal(new[] { "GameManager.cs" }, instantiations);

        var manager = Read("Scripts/GameManager.cs");
        Assert.Contains("public InputBindingService InputBindings { get; private set; }", manager, StringComparison.Ordinal);
    }

    [Fact]
    public void Initialization_HappensInEnterTreeBeforeNetworkSetup()
    {
        var manager = Read("Scripts/GameManager.cs");
        var enterTree = manager.IndexOf("void _EnterTree()", StringComparison.Ordinal);
        Assert.True(enterTree >= 0, "_EnterTree not found in GameManager.cs");
        var nextMember = manager.IndexOf("public override void _Process", enterTree, StringComparison.Ordinal);
        Assert.True(nextMember > enterTree, "_Process not found after _EnterTree in GameManager.cs");
        var body = manager.Substring(enterTree, nextMember - enterTree);

        Assert.Contains("ProjectSettings.GlobalizePath(\"user://input-bindings.json\")", body, StringComparison.Ordinal);
        Assert.Contains("InputBindings.Initialize()", body, StringComparison.Ordinal);

        var initialize = body.IndexOf("InputBindings.Initialize()", StringComparison.Ordinal);
        var network = body.IndexOf("new PacketManager()", StringComparison.Ordinal);
        Assert.True(initialize > 0 && initialize < network, "Initialize must run before packet/network setup");
    }

    [Fact]
    public void OnlySurface_ContainsMutatingInputMapCalls()
    {
        foreach (var (file, text) in ScriptFiles())
        {
            if (file == "GodotInputMapSurface.cs")
            {
                Assert.Contains("InputMap.ActionAddEvent(", text, StringComparison.Ordinal);
                Assert.Contains("InputMap.ActionEraseEvents(", text, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("InputMap.ActionAddEvent(", text, StringComparison.Ordinal);
                Assert.DoesNotContain("InputMap.ActionEraseEvents(", text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Adapter_IsSoleProductionCallerOfSurface()
    {
        var surfaceInstantiations = ScriptFiles()
            .Where(f => f.Text.Contains("new GodotInputMapSurface(", StringComparison.Ordinal))
            .Select(f => f.File)
            .ToList();
        Assert.Equal(new[] { "GameManager.cs" }, surfaceInstantiations);

        var adapterReferences = ScriptFiles()
            .Where(f => f.Text.Contains("GodotInputMapAdapter", StringComparison.Ordinal))
            .Select(f => f.File)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "GameManager.cs", "GodotInputMapAdapter.cs" }, adapterReferences);
    }

    [Fact]
    public void CharacterSettings_HasNoBindingFileReference()
    {
        var settings = Read("Scripts/CharacterSettings.cs");

        Assert.DoesNotContain("input-bindings", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("InputBindingService", settings, StringComparison.Ordinal);
    }
}
