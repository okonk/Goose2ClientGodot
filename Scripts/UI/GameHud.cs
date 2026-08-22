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

        // 3. Tooltips on a dedicated high CanvasLayer so they always render above every
        //    window (mirrors Unity's tooltip Canvas sortingOrder 10000). The HUD itself
        //    sits in GameManager.UiLayer (Layer 1); 100 guarantees tooltips win even over
        //    runtime-created windows (Info/Quest). TooltipManager._Ready still sets Instance.
        var tooltipLayer = new CanvasLayer { Layer = 100 };
        AddChild(tooltipLayer);
        var tooltips = GD.Load<PackedScene>("res://Scenes/UI/Tooltips.tscn").Instantiate<Control>();
        tooltipLayer.AddChild(tooltips);

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
        // Spell targeting captures all keyboard input (Unity disables the Player input map).
        if (GameManager.Instance.IsTargeting)
            return;
        // Don't toggle windows or refocus while typing in chat.
        if (GetViewport().GuiGetFocusOwner() is LineEdit)
            return;

        // Godot also fires unmodified actions (Enter → StartChat) when a modified
        // variant (Alt+Enter → ToggleFullscreen) is pressed; Alt combos are reserved.
        if (@event is InputEventKey { AltPressed: true })
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
        else if (@event.IsActionPressed("PickUp"))
            GameManager.Instance.NetworkClient.Pickup();
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
        else if (@event.IsActionPressed("EmoteHeart"))
            SendEmote(1080, 8);
        else if (@event.IsActionPressed("EmoteQuestion"))
            SendEmote(1081, 8);
        else if (@event.IsActionPressed("EmoteDots"))
            SendEmote(1083, 8);
        else if (@event.IsActionPressed("EmotePoop"))
            SendEmote(1084, 9);
        else if (@event.IsActionPressed("EmoteSurprised"))
            SendEmote(1085, 9);
        else if (@event.IsActionPressed("EmoteSleep"))
            SendEmote(1086, 9);
        else if (@event.IsActionPressed("EmoteAnnoyed"))
            SendEmote(1087, 9);
        else if (@event.IsActionPressed("EmoteSweat"))
            SendEmote(1088, 10);
        else if (@event.IsActionPressed("EmoteMusic"))
            SendEmote(1089, 10);
        else if (@event.IsActionPressed("EmoteWink"))
            SendEmote(1091, 10);
        else if (@event.IsActionPressed("EmoteTrash"))
            SendEmote(1082, 8);
        else if (@event.IsActionPressed("EmoteDollar"))
            SendEmote(1090, 10);
        else if (@event.IsActionPressed("RefreshPosition"))
            GameManager.Instance.NetworkClient.Command("/refresh");
    }

    // Animation/graphic id pairs from Unity PlayerController.cs:32-43.
    private static void SendEmote(int animationId, int graphicFile)
        => GameManager.Instance.NetworkClient.Emote(animationId, graphicFile);
}
