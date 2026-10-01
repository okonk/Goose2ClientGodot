using System;
using System.IO;
using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class KeyBindingsIntegrationContractTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    private static int Count(string text, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    [Fact]
    public void GameHud_OwnsExactlyOneKeyBindingsWindowBeforeTheManagers()
    {
        string hud = Read("Scripts/UI/GameHud.cs");
        Assert.Contains("public KeyBindingsWindow KeyBindings { get; private set; }", hud);
        Assert.Contains("KeyBindings = Add<KeyBindingsWindow>(\"res://Scenes/UI/KeyBindingsWindow.tscn\");", hud);
        Assert.Equal(1, Count(hud, "Add<KeyBindingsWindow>"));
        Assert.Equal(3, Count(hud, "KeyBindingsWindow"));

        int ready = hud.IndexOf("public override void _Ready()", StringComparison.Ordinal);
        int window = hud.IndexOf("KeyBindings = Add<KeyBindingsWindow>", StringComparison.Ordinal);
        int quest = hud.IndexOf("new QuestWindowManager()", StringComparison.Ordinal);
        int info = hud.IndexOf("new InfoWindowCreator()", StringComparison.Ordinal);
        int option = hud.IndexOf("new OptionListWindowManager()", StringComparison.Ordinal);
        Assert.True(ready >= 0 && window > ready, "KeyBindingsWindow must be instantiated in _Ready");
        Assert.True(window < quest, "KeyBindingsWindow must be created before QuestWindowManager");
        Assert.True(window < info, "KeyBindingsWindow must be created before InfoWindowCreator");
        Assert.True(window < option, "KeyBindingsWindow must be created before OptionListWindowManager");
    }

    [Fact]
    public void OptionsCallback_IsWiredAfterBothWindowsAreAdded()
    {
        string hud = Read("Scripts/UI/GameHud.cs");
        Assert.Contains("Options.OpenKeyBindings = KeyBindings.Open;", hud);
        int wiring = hud.IndexOf("Options.OpenKeyBindings = KeyBindings.Open;", StringComparison.Ordinal);
        int optionsAdd = hud.IndexOf("Options = Add<OptionsWindow>", StringComparison.Ordinal);
        int keyBindingsAdd = hud.IndexOf("KeyBindings = Add<KeyBindingsWindow>", StringComparison.Ordinal);
        Assert.True(wiring > optionsAdd, "wiring must occur after OptionsWindow's AddChild ran _Ready");
        Assert.True(wiring > keyBindingsAdd, "wiring must occur after KeyBindingsWindow's AddChild ran _Ready");
    }

    [Fact]
    public void Options_NeverLoadsOrInstantiatesKeyBindingsWindowItself()
    {
        string options = Read("Scripts/UI/OptionsWindow.cs");
        Assert.DoesNotContain("KeyBindingsWindow", options);
        Assert.Contains("public Action? OpenKeyBindings", options);
        Assert.Contains("OpenKeyBindings?.Invoke()", options);
    }

    [Fact]
    public void KeyBindings_IsADialog_AndHasNoHardcodedDefaultPosition()
    {
        Assert.True(DefaultWindowLayout.IsDialog("KeyBindings"));
        string layout = Read("Scripts/UI/DefaultWindowLayout.cs");
        Assert.DoesNotContain("[\"KeyBindings\"]", layout);
        Assert.Equal(new Vector2(240, 112), DefaultWindowLayout.LegacySize("Options"));
    }

    [Fact]
    public void ResetLayout_DiscoversKeyBindingsThroughHudWindows_WithoutSpecialCase()
    {
        string gameManager = Read("Scripts/GameManager.cs");
        Assert.Contains("public IEnumerable<BaseWindow> HudWindows()", gameManager);
        Assert.Contains("foreach (var d in CollectBaseWindows(child))", gameManager);

        string options = Read("Scripts/UI/OptionsWindow.cs");
        int reset = options.IndexOf("private void OnResetLayoutPressed()", StringComparison.Ordinal);
        int next = options.IndexOf("public override void Relayout()", reset, StringComparison.Ordinal);
        string body = options.Substring(reset, next - reset);
        Assert.Contains("GameManager.Instance.HudWindows()", body);
        Assert.DoesNotContain("KeyBindings", body);
    }

    [Fact]
    public void Window_HidesAfterBaseReady_SoSavedVisibleCannotAutoOpen()
    {
        string window = Read("Scripts/UI/KeyBindingsWindow.cs");
        int ready = window.IndexOf("public override void _Ready()", StringComparison.Ordinal);
        int baseReady = window.IndexOf("base._Ready();", ready, StringComparison.Ordinal);
        int hide = window.IndexOf("Visible = false;", baseReady, StringComparison.Ordinal);
        int endReady = window.IndexOf("public override void _ExitTree()", ready, StringComparison.Ordinal);
        Assert.True(hide > baseReady && hide < endReady, "window must explicitly hide after base._Ready");
    }

    [Fact]
    public void Open_WhileVisible_OnlyActivatesAndPreservesDraft()
    {
        string window = Read("Scripts/UI/KeyBindingsWindow.cs");
        int open = window.IndexOf("public void Open()", StringComparison.Ordinal);
        int next = window.IndexOf("\n    private", open + 1, StringComparison.Ordinal);
        string body = window.Substring(open, next - open);
        Assert.Contains("if (Visible)\n        {\n            Activate();\n            return;\n        }", body);
    }

    [Fact]
    public void Open_WhileGateHeld_DefersToServiceRestoration_AndCoalescesRepeatedPresses()
    {
        string window = Read("Scripts/UI/KeyBindingsWindow.cs");
        int open = window.IndexOf("public void Open()", StringComparison.Ordinal);
        int next = window.IndexOf("\n    private", open + 1, StringComparison.Ordinal);
        string body = window.Substring(open, next - open);
        Assert.Contains("CaptureGateHeld", body);
        Assert.Contains("_pendingOpen = true", body);
        Assert.DoesNotContain("RequestSafeRestore", body);
        Assert.DoesNotContain("RequestRestore", body);

        int released = window.IndexOf("private void OnCaptureGateReleased()", StringComparison.Ordinal);
        int releasedEnd = window.IndexOf("\n    public override void Relayout()", released, StringComparison.Ordinal);
        string releasedBody = window.Substring(released, releasedEnd - released);
        Assert.Contains("if (!_pendingOpen)", releasedBody);
        Assert.Contains("_pendingOpen = false", releasedBody);
        Assert.Contains("_editor!.Open()", releasedBody);
    }

    [Fact]
    public void BindingData_StayGlobal_GeometryStaysPerCharacter()
    {
        string window = Read("Scripts/UI/KeyBindingsWindow.cs");
        Assert.DoesNotContain("CharacterSettings", window);
        Assert.Contains("GameManager.Instance.InputBindings", window);
    }
}
