using System.Collections.Generic;

namespace Goose2Client.InputBindings;

public enum BindingContext
{
    Normal,
    Targeting,
    Global
}

public sealed record BindingRole(BindingContext Context, string Role);

public sealed record InputActionDefinition(string Name, string Label, string Category, IReadOnlyList<BindingRole> Roles);

public static class InputActionCatalog
{
    public static IReadOnlyList<InputActionDefinition> Actions { get; } =
    [
        new("MoveUp", "Move Up", "Movement",
            [new(BindingContext.Normal, "MoveUp"), new(BindingContext.Targeting, "TargetPrevious")]),
        new("MoveDown", "Move Down", "Movement",
            [new(BindingContext.Normal, "MoveDown"), new(BindingContext.Targeting, "TargetNext")]),
        new("MoveLeft", "Move Left", "Movement",
            [new(BindingContext.Normal, "MoveLeft"), new(BindingContext.Targeting, "TargetPrevious")]),
        new("MoveRight", "Move Right", "Movement",
            [new(BindingContext.Normal, "MoveRight"), new(BindingContext.Targeting, "TargetNext")]),
        new("Attack", "Attack", "Combat & Interaction",
            [new(BindingContext.Normal, "Attack")]),
        new("PickUp", "Pick Up", "Combat & Interaction",
            [new(BindingContext.Normal, "PickUp")]),
        new("ToggleMount", "Toggle Mount", "Combat & Interaction",
            [new(BindingContext.Normal, "ToggleMount")]),
        new("ToggleInventory", "Inventory", "Windows",
            [new(BindingContext.Normal, "ToggleInventory")]),
        new("ToggleSpellbook", "Spellbook", "Windows",
            [new(BindingContext.Normal, "ToggleSpellbook")]),
        new("ToggleCharacterWindow", "Character", "Windows",
            [new(BindingContext.Normal, "ToggleCharacterWindow")]),
        new("ToggleChat", "Chat", "Windows",
            [new(BindingContext.Normal, "ToggleChat")]),
        new("Hotkey1", "Hotbar Slot 1", "Hotbar",
            [new(BindingContext.Normal, "HotbarSlot1"), new(BindingContext.Targeting, "TargetConfirm")]),
        new("Hotkey2", "Hotbar Slot 2", "Hotbar",
            [new(BindingContext.Normal, "HotbarSlot2"), new(BindingContext.Targeting, "TargetConfirm")]),
        new("Hotkey3", "Hotbar Slot 3", "Hotbar",
            [new(BindingContext.Normal, "HotbarSlot3"), new(BindingContext.Targeting, "TargetConfirm")]),
        new("Hotkey4", "Hotbar Slot 4", "Hotbar",
            [new(BindingContext.Normal, "HotbarSlot4"), new(BindingContext.Targeting, "TargetConfirm")]),
        new("Hotkey5", "Hotbar Slot 5", "Hotbar",
            [new(BindingContext.Normal, "HotbarSlot5"), new(BindingContext.Targeting, "TargetConfirm")]),
        new("Hotkey6", "Hotbar Slot 6", "Hotbar",
            [new(BindingContext.Normal, "HotbarSlot6"), new(BindingContext.Targeting, "TargetConfirm")]),
        new("Hotkey7", "Hotbar Slot 7", "Hotbar",
            [new(BindingContext.Normal, "HotbarSlot7"), new(BindingContext.Targeting, "TargetConfirm")]),
        new("Hotkey8", "Hotbar Slot 8", "Hotbar",
            [new(BindingContext.Normal, "HotbarSlot8"), new(BindingContext.Targeting, "TargetConfirm")]),
        new("Hotkey9", "Hotbar Slot 9", "Hotbar",
            [new(BindingContext.Normal, "HotbarSlot9"), new(BindingContext.Targeting, "TargetConfirm")]),
        new("Hotkey0", "Hotbar Slot 10", "Hotbar",
            [new(BindingContext.Normal, "HotbarSlot10"), new(BindingContext.Targeting, "TargetConfirm")]),
        new("CycleHotbarPage", "Cycle Hotbar Page", "Hotbar",
            [new(BindingContext.Normal, "CycleHotbarPage")]),
        new("StartChat", "Start Chat", "Chat",
            [new(BindingContext.Normal, "StartChat")]),
        new("SlashCommand", "Slash Command", "Chat",
            [new(BindingContext.Normal, "SlashCommand")]),
        new("GuildCommand", "Guild Command", "Chat",
            [new(BindingContext.Normal, "GuildCommand")]),
        new("TellCommand", "Tell Command", "Chat",
            [new(BindingContext.Normal, "TellCommand")]),
        new("ReplyCommand", "Reply", "Chat",
            [new(BindingContext.Normal, "ReplyCommand")]),
        new("ScrollChatUp", "Scroll Chat Up", "Chat",
            [new(BindingContext.Normal, "ScrollChatUp")]),
        new("ScrollChatDown", "Scroll Chat Down", "Chat",
            [new(BindingContext.Normal, "ScrollChatDown")]),
        new("TargetUp", "Previous Target", "Targeting",
            [new(BindingContext.Targeting, "TargetPrevious")]),
        new("TargetDown", "Next Target", "Targeting",
            [new(BindingContext.Targeting, "TargetNext")]),
        new("ConfirmTarget", "Confirm Target", "Targeting",
            [new(BindingContext.Targeting, "TargetConfirm")]),
        new("TargetHome", "Target Self", "Targeting",
            [new(BindingContext.Targeting, "TargetHome")]),
        new("CancelTarget", "Cancel Targeting", "Targeting",
            [new(BindingContext.Targeting, "CancelTargeting")]),
        new("EmoteHeart", "Heart", "Emotes",
            [new(BindingContext.Normal, "EmoteHeart")]),
        new("EmoteQuestion", "Question", "Emotes",
            [new(BindingContext.Normal, "EmoteQuestion")]),
        new("EmoteDots", "Dots", "Emotes",
            [new(BindingContext.Normal, "EmoteDots")]),
        new("EmotePoop", "Poop", "Emotes",
            [new(BindingContext.Normal, "EmotePoop")]),
        new("EmoteSurprised", "Surprised", "Emotes",
            [new(BindingContext.Normal, "EmoteSurprised")]),
        new("EmoteSleep", "Sleep", "Emotes",
            [new(BindingContext.Normal, "EmoteSleep")]),
        new("EmoteAnnoyed", "Annoyed", "Emotes",
            [new(BindingContext.Normal, "EmoteAnnoyed")]),
        new("EmoteSweat", "Sweat", "Emotes",
            [new(BindingContext.Normal, "EmoteSweat")]),
        new("EmoteMusic", "Music", "Emotes",
            [new(BindingContext.Normal, "EmoteMusic")]),
        new("EmoteWink", "Wink", "Emotes",
            [new(BindingContext.Normal, "EmoteWink")]),
        new("EmoteTrash", "Trash", "Emotes",
            [new(BindingContext.Normal, "EmoteTrash")]),
        new("EmoteDollar", "Dollar", "Emotes",
            [new(BindingContext.Normal, "EmoteDollar")]),
        new("RefreshPosition", "Refresh Position", "System",
            [new(BindingContext.Normal, "RefreshPosition")]),
        new("ToggleFullscreen", "Toggle Fullscreen", "System",
            [new(BindingContext.Normal, "ToggleFullscreen"), new(BindingContext.Targeting, "ToggleFullscreen")])
    ];

    public static bool IsRemappable(string name) => Find(name) != null;

    public static InputActionDefinition? Find(string name)
    {
        foreach (var action in Actions)
            if (action.Name == name)
                return action;

        return null;
    }
}
