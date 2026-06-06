using Godot;

namespace Goose2Client.UI;

/// <summary>
/// Root node for all persistent HUD windows. Instantiates every window, tooltip,
/// world drop target, and multi-window manager in _Ready. Routes input toggles.
/// Attached to GameManager.UiLayer and survives map swaps.
/// </summary>
public partial class GameHud : Control
{
    // --- Typed public accessors (settable private) ---
    public VitalsWindow Vitals { get; private set; }
    public InventoryWindow Inventory { get; private set; }
    public CharacterWindow Character { get; private set; }
    public SpellbookWindow Spellbook { get; private set; }
    public HotbarWindow Hotbar { get; private set; }
    public ChatWindow Chat { get; private set; }
    public PartyWindow Party { get; private set; }
    public BuffEffectsWindow Buffs { get; private set; }
    public DebugWindow Debug { get; private set; }
    public OptionsWindow Options { get; private set; }
    public VendorWindow Vendor { get; private set; }
    public BankWindow Bank { get; private set; }
    public CombineBagContainerWindow CombineBag { get; private set; }

    /// <summary>Instantiate a scene and add it as a child, returning the typed node.</summary>
    private T Add<T>(string path) where T : Node
    {
        var n = GD.Load<PackedScene>(path).Instantiate<T>();
        AddChild(n);
        return n;
    }

    public override void _Ready()
    {
        // 1. Fill the screen; ignore mouse so child windows handle their own input.
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // 2. World drop target FIRST (sits behind windows).
        var drop = new WorldDropTarget();
        drop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(drop);

        // 3. Tooltips (sets TooltipManager.Instance).
        Add<Control>("res://Scenes/UI/Tooltips.tscn");

        // 4. Instantiate each window scene.
        Vitals = Add<VitalsWindow>("res://Scenes/UI/VitalsWindow.tscn");
        Inventory = Add<InventoryWindow>("res://Scenes/UI/InventoryWindow.tscn");
        Character = Add<CharacterWindow>("res://Scenes/UI/CharacterWindow.tscn");
        Spellbook = Add<SpellbookWindow>("res://Scenes/UI/SpellbookWindow.tscn");
        Hotbar = Add<HotbarWindow>("res://Scenes/UI/HotbarWindow.tscn");
        var toolbar = Add<Control>("res://Scenes/UI/Toolbar.tscn");
        Chat = Add<ChatWindow>("res://Scenes/UI/ChatWindow.tscn");
        Party = Add<PartyWindow>("res://Scenes/UI/PartyWindow.tscn");
        Buffs = Add<BuffEffectsWindow>("res://Scenes/UI/BuffEffectsWindow.tscn");
        Debug = Add<DebugWindow>("res://Scenes/UI/DebugWindow.tscn");
        Options = Add<OptionsWindow>("res://Scenes/UI/OptionsWindow.tscn");
        Vendor = Add<VendorWindow>("res://Scenes/UI/VendorWindow.tscn");
        Bank = Add<BankWindow>("res://Scenes/UI/BankWindow.tscn");
        CombineBag = Add<CombineBagContainerWindow>("res://Scenes/UI/CombineBagContainerWindow.tscn");

        // 5. Multi-window managers (plain Node subclasses, instantiable via new).
        AddChild(new QuestWindowManager());
        AddChild(new InfoWindowCreator());

        // 6. Wire cross-references (after AddChild so each node's _Ready has run).
        Hotbar.InventoryWindow = Inventory;
        Hotbar.SpellbookWindow = Spellbook;

        // Toolbar Options button → OptionsWindow toggle.
        var optionsBtn = toolbar.GetNodeOrNull<ToolbarItem>("OptionsButton");
        if (optionsBtn != null)
            optionsBtn.OnOptions = Options.ToggleWindow;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Don't toggle windows or refocus while typing in chat.
        if (GetViewport().GuiGetFocusOwner() is LineEdit)
            return;

        if (@event.IsActionPressed("ToggleInventory"))
            Inventory.Toggle();
        else if (@event.IsActionPressed("ToggleSpellbook"))
            Spellbook.Toggle();
        else if (@event.IsActionPressed("ToggleCharacterWindow"))
            Character.Toggle();
        else if (@event.IsActionPressed("CycleHotbarPage"))
            Hotbar.CyclePage();
        else if (@event.IsActionPressed("ToggleMount"))
            Hotbar.ToggleMount();
        else if (@event.IsActionPressed("StartChat"))
            Chat.FocusChat("");
        else if (@event.IsActionPressed("SlashCommand"))
            Chat.FocusChat("/");
        else if (@event.IsActionPressed("GuildCommand"))
            Chat.FocusChat("/guild ");
        else if (@event.IsActionPressed("TellCommand"))
            Chat.FocusChat("/tell ");
        else if (@event.IsActionPressed("ReplyCommand"))
            Chat.FocusChat(Chat.ReplyToName == null ? "/tell " : $"/tell {Chat.ReplyToName} ");
    }
}
