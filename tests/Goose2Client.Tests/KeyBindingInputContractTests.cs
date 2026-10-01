using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Goose2Client.InputBindings;
using Xunit;

namespace Goose2Client.Tests;

public class KeyBindingInputContractTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    private static readonly Regex LiteralMatchCall = new(
        @"\.IsAction(?:Just)?Pressed\(\s*""([^""]+)""", RegexOptions.Compiled);

    private static void AssertEveryLiteralCallIsExactAndCataloged(string source, string file)
    {
        var matches = LiteralMatchCall.Matches(source).Cast<Match>().ToList();
        Assert.NotEmpty(matches);
        foreach (var m in matches)
        {
            string action = m.Groups[1].Value;
            Assert.True(InputActionCatalog.IsRemappable(action),
                $"{file} matches action \"{action}\" that is not in the catalog");

            int rest = m.Index + m.Length;
            int comma = source.IndexOf(',', rest);
            int close = source.IndexOf(')', rest);
            if (close < rest || (comma >= 0 && comma < close))
            {
                int end = source.IndexOfAny(new[] { ',', ')' }, comma + 1);
                string firstArg = source.Substring(comma + 1, end - comma - 1).Trim();
                Assert.True(firstArg.StartsWith("exactMatch: true"),
                    $"{file} matches \"{action}\" without exactMatch: true");
            }
            else
            {
                Assert.Fail($"{file} matches \"{action}\" with no arguments");
            }
        }
    }

    [Fact]
    public void GameHud_DoesNotDiscardAllAltChords()
    {
        string hud = Read("Scripts/UI/GameHud.cs");
        Assert.DoesNotContain("AltPressed", hud);
    }

    [Fact]
    public void GameHud_MatchesEveryCatalogLiteralExactly()
    {
        string hud = Read("Scripts/UI/GameHud.cs");
        AssertEveryLiteralCallIsExactAndCataloged(hud, "GameHud.cs");
        foreach (var action in new[] { "ToggleInventory", "ToggleSpellbook", "CycleHotbarPage", "StartChat", "EmoteHeart", "EmoteDollar" })
            Assert.Contains($"IsActionPressed(\"{action}\", exactMatch: true)", hud);
    }

    [Fact]
    public void GameHud_RetainsTargetingAndLineEditGuardsBeforeCatalogMatches()
    {
        string hud = Read("Scripts/UI/GameHud.cs");
        int input = hud.IndexOf("public override void _UnhandledInput(", StringComparison.Ordinal);
        int next = hud.IndexOf("private static void SendEmote(", input, StringComparison.Ordinal);
        Assert.True(input >= 0 && next > input, "raw-input handler must precede SendEmote");
        string body = hud.Substring(input, next - input);

        int targeting = body.IndexOf("GameManager.Instance.IsTargeting", StringComparison.Ordinal);
        int lineEdit = body.IndexOf("GetViewport().GuiGetFocusOwner() is LineEdit", StringComparison.Ordinal);
        int firstMatch = body.IndexOf("IsActionPressed", StringComparison.Ordinal);
        Assert.True(targeting >= 0, "IsTargeting guard must remain in the raw-input handler");
        Assert.True(lineEdit > targeting, "LineEdit focus guard must remain after the IsTargeting guard");
        Assert.True(firstMatch > lineEdit, "catalog matches must run after both guards");
    }

    [Fact]
    public void GameManager_Fullscreen_IsExactAndSkippedWhileTypingInChat()
    {
        string gm = Read("Scripts/GameManager.cs");
        AssertEveryLiteralCallIsExactAndCataloged(gm, "GameManager.cs");

        int input = gm.IndexOf("public override void _Input(", StringComparison.Ordinal);
        int next = gm.IndexOf("public void ToggleFullscreen()", input, StringComparison.Ordinal);
        Assert.True(input >= 0 && next > input, "_Input must precede ToggleFullscreen");
        string body = gm.Substring(input, next - input);
        Assert.Contains("GetTree().Root.GuiGetFocusOwner() is LineEdit", body);
        int guard = body.IndexOf("GuiGetFocusOwner", StringComparison.Ordinal);
        int match = body.IndexOf("IsActionPressed", guard, StringComparison.Ordinal);
        Assert.True(match > guard, "fullscreen match must run after the root LineEdit focus guard");
    }

    [Fact]
    public void SpellTargetManager_CyclingIsExactWithEcho_OthersExactWithoutEcho()
    {
        string stm = Read("Scripts/SpellTargetManager.cs");
        foreach (var action in new[] { "TargetUp", "MoveUp", "MoveLeft", "TargetDown", "MoveDown", "MoveRight" })
            Assert.Contains($"IsActionPressed(\"{action}\", exactMatch: true, allowEcho: true)", stm);
        foreach (var action in new[] { "ConfirmTarget", "CancelTarget", "TargetHome" })
        {
            Assert.Contains($"IsActionPressed(\"{action}\", exactMatch: true)", stm);
            Assert.DoesNotContain($"IsActionPressed(\"{action}\", exactMatch: true, allowEcho: true)", stm);
        }
    }

    [Fact]
    public void Character_HeldMovementAndAttackPollsAreExact()
    {
        string c = Read("Scripts/Character/Character.cs");
        AssertEveryLiteralCallIsExactAndCataloged(c, "Character.cs");
        foreach (var action in new[] { "Attack", "MoveUp", "MoveDown", "MoveLeft", "MoveRight" })
        {
            int total = Regex.Matches(c, $"Input\\.IsActionPressed\\(\"{action}\"").Count;
            int exact = Regex.Matches(c, $"Input\\.IsActionPressed\\(\"{action}\", exactMatch: true\\)").Count;
            Assert.True(total > 0, $"Character must poll {action}");
            Assert.Equal(total, exact);
        }
    }

    [Fact]
    public void HotbarWindow_TargetHotkeyExactChecksRemain()
    {
        string hotbar = Read("Scripts/UI/HotbarWindow.cs");
        Assert.Contains("Input.IsActionJustPressed(action, exactMatch: true)", hotbar);
        Assert.Contains("Input.IsActionPressed(action, exactMatch: true)", hotbar);
    }

    [Fact]
    public void FixedChatStackSplitPointerAndClickBehavior_StaysDirectAndOutOfCatalog()
    {
        string chat = Read("Scripts/UI/ChatWindow.cs");
        Assert.Contains("k.Keycode == Key.Up", chat);
        Assert.Contains("k.Keycode == Key.Down", chat);
        Assert.Contains("k.Keycode == Key.Escape", chat);
        Assert.DoesNotContain("IsActionPressed", chat);

        string helpers = Read("Scripts/Helpers.Godot.cs");
        Assert.Contains("Input.IsKeyPressed(Key.Ctrl)", helpers);
        Assert.Contains("Input.IsKeyPressed(Key.Shift)", helpers);
        Assert.DoesNotContain("IsActionPressed", helpers);

        string baseWindow = Read("Scripts/UI/BaseWindow.cs");
        Assert.Contains("InputEventMouseButton", baseWindow);

        string map = Read("Scripts/MapManager.cs");
        Assert.Contains("MouseButton.Left", map);

        string names = string.Join(" ", InputActionCatalog.Actions.Select(a => a.Name));
        foreach (var token in new[] { "History", "Stack", "Drag", "Resize", "Click" })
            Assert.DoesNotContain(token, names);
    }
}
