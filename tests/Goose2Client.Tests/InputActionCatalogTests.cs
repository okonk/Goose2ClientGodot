using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Goose2Client.InputBindings;
using Xunit;

namespace Goose2Client.Tests;

public class InputActionCatalogTests
{
    private static readonly string[] ExpectedActions =
    {
        "MoveUp", "MoveDown", "MoveLeft", "MoveRight",
        "Attack", "PickUp", "ToggleMount",
        "ToggleInventory", "ToggleSpellbook", "ToggleCharacterWindow", "ToggleChat",
        "Hotkey1", "Hotkey2", "Hotkey3", "Hotkey4", "Hotkey5", "Hotkey6", "Hotkey7", "Hotkey8", "Hotkey9", "Hotkey0", "CycleHotbarPage",
        "StartChat", "SlashCommand", "GuildCommand", "TellCommand", "ReplyCommand", "ScrollChatUp", "ScrollChatDown",
        "TargetUp", "TargetDown", "ConfirmTarget", "TargetHome", "CancelTarget",
        "EmoteHeart", "EmoteQuestion", "EmoteDots", "EmotePoop", "EmoteSurprised", "EmoteSleep",
        "EmoteAnnoyed", "EmoteSweat", "EmoteMusic", "EmoteWink", "EmoteTrash", "EmoteDollar",
        "RefreshPosition", "ToggleFullscreen"
    };

    private static readonly Dictionary<string, string> ExpectedCategories = new()
    {
        ["MoveUp"] = "Movement", ["MoveDown"] = "Movement", ["MoveLeft"] = "Movement", ["MoveRight"] = "Movement",
        ["Attack"] = "Combat & Interaction", ["PickUp"] = "Combat & Interaction", ["ToggleMount"] = "Combat & Interaction",
        ["ToggleInventory"] = "Windows", ["ToggleSpellbook"] = "Windows", ["ToggleCharacterWindow"] = "Windows", ["ToggleChat"] = "Windows",
        ["Hotkey1"] = "Hotbar", ["Hotkey2"] = "Hotbar", ["Hotkey3"] = "Hotbar", ["Hotkey4"] = "Hotbar",
        ["Hotkey5"] = "Hotbar", ["Hotkey6"] = "Hotbar", ["Hotkey7"] = "Hotbar", ["Hotkey8"] = "Hotbar",
        ["Hotkey9"] = "Hotbar", ["Hotkey0"] = "Hotbar", ["CycleHotbarPage"] = "Hotbar",
        ["StartChat"] = "Chat", ["SlashCommand"] = "Chat", ["GuildCommand"] = "Chat", ["TellCommand"] = "Chat", ["ReplyCommand"] = "Chat",
        ["ScrollChatUp"] = "Chat", ["ScrollChatDown"] = "Chat",
        ["TargetUp"] = "Targeting", ["TargetDown"] = "Targeting", ["ConfirmTarget"] = "Targeting",
        ["TargetHome"] = "Targeting", ["CancelTarget"] = "Targeting",
        ["EmoteHeart"] = "Emotes", ["EmoteQuestion"] = "Emotes", ["EmoteDots"] = "Emotes", ["EmotePoop"] = "Emotes",
        ["EmoteSurprised"] = "Emotes", ["EmoteSleep"] = "Emotes", ["EmoteAnnoyed"] = "Emotes",
        ["EmoteSweat"] = "Emotes", ["EmoteMusic"] = "Emotes", ["EmoteWink"] = "Emotes",
        ["EmoteTrash"] = "Emotes", ["EmoteDollar"] = "Emotes",
        ["RefreshPosition"] = "System", ["ToggleFullscreen"] = "System"
    };

    [Fact]
    public void Catalog_ContainsExactlyThe48ApprovedActions_InDesignCategoryAndDisplayOrder()
    {
        var actions = InputActionCatalog.Actions;

        Assert.Equal(48, actions.Count);
        Assert.Equal(ExpectedActions, actions.Select(a => a.Name).ToArray());

        foreach (var action in actions)
            Assert.Equal(ExpectedCategories[action.Name], action.Category);
    }

    [Fact]
    public void Names_Labels_AndCategoryLabels_AreNonEmpty_AndNamesAreUnique()
    {
        var actions = InputActionCatalog.Actions;

        Assert.All(actions, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
        Assert.All(actions, a => Assert.False(string.IsNullOrWhiteSpace(a.Label)));
        Assert.All(actions, a => Assert.False(string.IsNullOrWhiteSpace(a.Category)));
        Assert.Equal(actions.Count, actions.Select(a => a.Name).Distinct().Count());
    }

    [Fact]
    public void EveryCatalogName_IsDeclaredAsAnInputAction_InProjectGodot()
    {
        var projectActions = ParseProjectInputActions();

        foreach (var action in InputActionCatalog.Actions)
            Assert.True(projectActions.Contains(action.Name), $"catalog action {action.Name} missing from [input] in project.godot");
    }

    [Fact]
    public void UnusedAndUiPointerAndTrackedDeviceActions_AreAbsent()
    {
        var names = InputActionCatalog.Actions.Select(a => a.Name).ToHashSet();

        foreach (var excluded in new[]
        {
            "Move", "Navigate", "Submit", "Cancel",
            "Click", "MiddleClick", "RightClick", "Point", "ScrollWheel",
            "TrackedDevicePosition", "TrackedDeviceOrientation"
        })
            Assert.False(names.Contains(excluded), $"{excluded} must not be remappable");
    }

    [Fact]
    public void MovementDirections_HaveDistinctNormalRoles()
    {
        var roles = InputActionCatalog.Actions
            .Where(a => a.Name is "MoveUp" or "MoveDown" or "MoveLeft" or "MoveRight")
            .SelectMany(a => a.Roles.Where(r => r.Context == BindingContext.Normal).Select(r => r.Role))
            .ToArray();

        Assert.Equal(4, roles.Distinct().Count());
    }

    [Fact]
    public void Targeting_MovementAliases_ShareTargetPreviousAndTargetNextRoles()
    {
        var previous = RoleNames("MoveUp", "MoveLeft", "TargetUp");
        var next = RoleNames("MoveDown", "MoveRight", "TargetDown");

        Assert.Contains("TargetPrevious", previous);
        Assert.Contains("TargetPrevious", RoleNames("MoveUp"));
        Assert.Contains("TargetPrevious", RoleNames("MoveLeft"));
        Assert.Contains("TargetPrevious", RoleNames("TargetUp"));
        Assert.Contains("TargetNext", next);
        Assert.Contains("TargetNext", RoleNames("MoveDown"));
        Assert.Contains("TargetNext", RoleNames("MoveRight"));
        Assert.Contains("TargetNext", RoleNames("TargetDown"));
    }

    [Fact]
    public void HotbarActions_HaveDistinctSlotRoles_AndShareTargetConfirmWithConfirmTarget()
    {
        var hotbarActions = InputActionCatalog.Actions
            .Where(a => a.Name.StartsWith("Hotkey"))
            .ToArray();

        Assert.Equal(10, hotbarActions.Length);

        var slotRoles = hotbarActions
            .SelectMany(a => a.Roles.Where(r => r.Context == BindingContext.Normal).Select(r => r.Role))
            .ToArray();
        Assert.Equal(10, slotRoles.Distinct().Count());

        foreach (var action in hotbarActions)
        {
            var targetingRoles = action.Roles
                .Where(r => r.Context == BindingContext.Targeting)
                .Select(r => r.Role)
                .ToArray();
            Assert.Contains("TargetConfirm", targetingRoles);
        }

        var confirmTargetRoles = RoleNames(new[] { "ConfirmTarget" }, BindingContext.Targeting);
        Assert.Contains("TargetConfirm", confirmTargetRoles);
    }

    [Fact]
    public void TargetHomeAndCancelTargeting_HaveDistinctTargetingRoles()
    {
        var home = RoleNames(new[] { "TargetHome" }, BindingContext.Targeting);
        var cancel = RoleNames(new[] { "CancelTarget" }, BindingContext.Targeting);

        Assert.Single(home);
        Assert.Single(cancel);
        Assert.NotEqual(home[0], cancel[0]);
        Assert.DoesNotContain("TargetConfirm", home);
        Assert.DoesNotContain("TargetConfirm", cancel);
    }

    [Fact]
    public void ToggleFullscreen_HasADistinctRoleInBothNormalAndTargetingContexts()
    {
        var normal = RoleNames(new[] { "ToggleFullscreen" }, BindingContext.Normal);
        var targeting = RoleNames(new[] { "ToggleFullscreen" }, BindingContext.Targeting);

        Assert.Single(normal);
        Assert.Single(targeting);
        Assert.Equal(normal[0], targeting[0]);

        var other = InputActionCatalog.Actions
            .Where(a => a.Name != "ToggleFullscreen")
            .SelectMany(a => a.Roles)
            .Where(r => r.Role == normal[0])
            .ToArray();
        Assert.Empty(other);
    }

    private static string[] RoleNames(params string[] actionNames) =>
        RoleNames(actionNames, null);

    private static string[] RoleNames(string[] actionNames, BindingContext? context)
    {
        var names = actionNames.ToHashSet();
        return InputActionCatalog.Actions
            .Where(a => names.Contains(a.Name))
            .SelectMany(a => a.Roles.Where(r => context is null || r.Context == context))
            .Select(r => r.Role)
            .ToArray();
    }

    private static HashSet<string> ParseProjectInputActions()
    {
        var path = Path.Combine(RepositoryRoot(), "project.godot");
        var lines = File.ReadAllLines(path);
        var actions = new HashSet<string>();
        var inInput = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("["))
            {
                inInput = trimmed == "[input]";
                continue;
            }

            if (!inInput)
                continue;

            var match = Regex.Match(line, "^([A-Za-z_][A-Za-z0-9_]*)=");
            if (match.Success)
                actions.Add(match.Groups[1].Value);
        }

        return actions;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory!.FullName;
    }
}
