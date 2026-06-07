using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Character window — 14 equipped-item slots plus stat/level/XP readout.
/// Mirrors Unity CharacterWindow with Godot-specific layout.
/// </summary>
public partial class CharacterWindow : BaseWindow, IWindow
{
    public const int EquippedSlotCount = 14;
    private const int FirstSlotNumber = 31;

    private static readonly PackedScene SlotScene = GD.Load<PackedScene>("res://Scenes/UI/ItemSlot.tscn");

    private ItemSlot[] _slots;
    private bool _listenersRegistered;

    // Character info labels
    private Label _nameText;
    private Label _levelClassText;
    private Label _guildText;
    private Label _experienceText;
    private Label _experienceSoldText;

    // Stat labels
    private Label _strengthText;
    private Label _staminaText;
    private Label _intelligenceText;
    private Label _dexterityText;
    private Label _acText;

    // Resist labels
    private Label _fireResistText;
    private Label _waterResistText;
    private Label _earthResistText;
    private Label _airResistText;
    private Label _spiritResistText;

    public int WindowId => (int)WindowFrame;
    public WindowFrames WindowFrame => WindowFrames.Equipped;

    public override void _Ready()
    {
        base._Ready();

        // Resolve labels
        _nameText = GetNode<Label>("Content/NameText");
        _levelClassText = GetNode<Label>("Content/LevelClassText");
        _guildText = GetNode<Label>("Content/GuildText");
        _experienceText = GetNode<Label>("Content/ExperienceText");
        _experienceSoldText = GetNode<Label>("Content/ExperienceSoldText");
        _strengthText = GetNode<Label>("Content/StrengthText");
        _staminaText = GetNode<Label>("Content/StaminaText");
        _intelligenceText = GetNode<Label>("Content/IntelligenceText");
        _dexterityText = GetNode<Label>("Content/DexterityText");
        _acText = GetNode<Label>("Content/ACText");
        _fireResistText = GetNode<Label>("Content/FireResistText");
        _waterResistText = GetNode<Label>("Content/WaterResistText");
        _earthResistText = GetNode<Label>("Content/EarthResistText");
        _airResistText = GetNode<Label>("Content/AirResistText");
        _spiritResistText = GetNode<Label>("Content/SpiritResistText");

        // Create 14 equipped slots — anatomical layout via CharacterEquipmentLayout
        _slots = new ItemSlot[EquippedSlotCount];
        var slotGrid = GetNode<Control>("Content/SlotGrid");

        for (int i = 0; i < EquippedSlotCount; i++)
        {
            var slot = SlotScene.Instantiate<ItemSlot>();
            slotGrid.AddChild(slot);
            slot.Position = CharacterEquipmentLayout.SlotOffset(i);
            slot.SlotNumber = i + FirstSlotNumber;
            slot.Window = this;
            slot.OnDoubleClick = UseItem;
            _slots[i] = slot;
        }

        // Register packet listeners
        GameManager.Instance.PacketManager.Listen<InventorySlotPacket>(OnInventorySlot);
        GameManager.Instance.PacketManager.Listen<ClearInventorySlotPacket>(OnClearInventorySlot);
        GameManager.Instance.PacketManager.Listen<StatusInfoPacket>(OnStatusInfo);
        GameManager.Instance.PacketManager.Listen<ExperienceBarPacket>(OnExperienceBar);
        _listenersRegistered = true;

        // First-login default: closed (toggle with I/C/B). Restore saved visibility if present.
        if (WindowName != null)
        {
            if (GameManager.Instance.CharacterSettings.GetWindowSettings(WindowName)?.Visible is not bool savedVisible)
                Visible = false;
            else
                Visible = savedVisible;
        }
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<InventorySlotPacket>(OnInventorySlot);
        GameManager.Instance.PacketManager.Remove<ClearInventorySlotPacket>(OnClearInventorySlot);
        GameManager.Instance.PacketManager.Remove<StatusInfoPacket>(OnStatusInfo);
        GameManager.Instance.PacketManager.Remove<ExperienceBarPacket>(OnExperienceBar);
    }

    private void OnInventorySlot(object o)
    {
        var p = (InventorySlotPacket)o;
        int idx = p.SlotNumber - FirstSlotNumber;
        if (idx < 0 || idx >= _slots.Length) return;
        _slots[idx].SetItem(ItemStats.FromPacket(p));
    }

    private void OnClearInventorySlot(object o)
    {
        var p = (ClearInventorySlotPacket)o;
        int idx = p.SlotNumber - FirstSlotNumber;
        if (idx < 0 || idx >= _slots.Length) return;
        _slots[idx].ClearItem();
    }

    public void UseItem(ItemStats stats)
    {
        GameManager.Instance.NetworkClient.UseItem(stats.SlotNumber);
    }

    private void OnStatusInfo(object o)
    {
        var p = (StatusInfoPacket)o;

        var lp = GameManager.Instance.CurrentMapManager?.LocalPlayer;
        if (lp != null)
            _nameText.Text = lp.CharacterName;

        _levelClassText.Text = $"Level {p.Level} {p.ClassName}";
        _guildText.Text = string.IsNullOrWhiteSpace(p.GuildName) ? "No Guild" : p.GuildName;

        _strengthText.Text = p.Strength.ToString();
        _staminaText.Text = p.Stamina.ToString();
        _intelligenceText.Text = p.Intelligence.ToString();
        _dexterityText.Text = p.Dexterity.ToString();
        _acText.Text = p.ArmorClass.ToString();

        _fireResistText.Text = p.FireResist.ToString();
        _waterResistText.Text = p.WaterResist.ToString();
        _earthResistText.Text = p.EarthResist.ToString();
        _airResistText.Text = p.AirResist.ToString();
        _spiritResistText.Text = p.SpiritResist.ToString();
    }

    private void OnExperienceBar(object o)
    {
        var p = (ExperienceBarPacket)o;
        _experienceText.Text = p.Experience.ToString("N0");
        _experienceSoldText.Text = p.ExperienceSold.ToString("N0");
    }
}
