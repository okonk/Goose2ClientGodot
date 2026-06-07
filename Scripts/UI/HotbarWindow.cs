using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Hotbar window — 3 pages × 10 slots = 30 slots, XP bar, mount tracking,
/// hotkey polling in _Process, drag-and-drop from inventory/spellbook.
/// </summary>
public partial class HotbarWindow : BaseWindow, IWindow
{
    public const int PageCount = 3;
    public const int SlotsPerPage = 10;
    private const int MountSlotNumber = 44;

    private static readonly PackedScene SlotScene = GD.Load<PackedScene>("res://Scenes/UI/HotbarSlot.tscn");

    private HotbarPage[] _pages;
    private int _pageIndex;
    private Button _backButton;
    private Button _nextButton;
    private TextureProgressBar _xpBar;
    private Label _xpText;
    private string _xpTooltip = "";
    private bool _mounted;
    private readonly Dictionary<int, string> _mountSlots = new();
    private float _repeatTimer;
    private Timer _saveTimer;
    private bool _listenersRegistered;

    public InventoryWindow InventoryWindow { get; set; }
    public SpellbookWindow SpellbookWindow { get; set; }

    public int WindowId => (int)WindowFrame;
    public WindowFrames WindowFrame => WindowFrames.Hotbar;

    protected override bool UseFixedDockLayout => true;

    public override void _Ready()
    {
        base._Ready();

        var pagesContainer = GetNode<Control>("Content/Pages");
        _backButton = GetNode<Button>("Content/BackButton");
        _nextButton = GetNode<Button>("Content/NextButton");
        _xpBar = GetNode<TextureProgressBar>("Content/XpBar");
        _xpBar.MaxValue = 1;
        _xpText = GetNode<Label>("Content/XpText");

        // XP bar tooltip. The XpText label overlaps the bar and (mouse_filter=Pass) eats the
        // hover, so wire the label too — otherwise hovering the number wouldn't show the tooltip.
        _xpBar.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_xpTooltip, _xpBar);
        _xpBar.MouseExited += () => TooltipManager.Instance.HideTextTooltip();
        _xpText.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_xpTooltip, _xpText);
        _xpText.MouseExited += () => TooltipManager.Instance.HideTextTooltip();

        // Back / Next buttons
        _backButton.Pressed += OnBackClicked;
        _nextButton.Pressed += OnNextClicked;

        // Save debounce timer
        _saveTimer = new Timer { OneShot = true, WaitTime = 5 };
        AddChild(_saveTimer);
        _saveTimer.Timeout += SaveSlots;

        // Create pages
        _pages = new HotbarPage[PageCount];
        var settings = GameManager.Instance.CharacterSettings.Hotkeys;

        for (int p = 0; p < PageCount; p++)
        {
            var grid = new GridContainer { Columns = SlotsPerPage };
            grid.AddThemeConstantOverride("h_separation", 1);
            grid.AddThemeConstantOverride("v_separation", 1);
            grid.Name = $"Page{p}";
            pagesContainer.AddChild(grid);

            var slots = new HotbarSlot[SlotsPerPage];
            for (int i = 0; i < SlotsPerPage; i++)
            {
                var slot = SlotScene.Instantiate<HotbarSlot>();
                grid.AddChild(slot);
                slot.SlotNumber = i;
                slot.Window = this;
                slot.OnUseSlot = UseSlot;
                slot.OnSaveSlots = SaveSlotsDelayed;
                LoadSlot(slot, settings[p * SlotsPerPage + i]);
                slots[i] = slot;
            }

            _pages[p] = new HotbarPage
            {
                Slots = slots,
                Container = grid
            };

            grid.Visible = (p == _pageIndex);
        }

        // Register packet listeners
        GameManager.Instance.PacketManager.Listen<ExperienceBarPacket>(OnExperienceBar);
        GameManager.Instance.PacketManager.Listen<InventorySlotPacket>(OnInventorySlot);
        GameManager.Instance.PacketManager.Listen<ClearInventorySlotPacket>(OnClearInventorySlot);
        GameManager.Instance.PacketManager.Listen<SpellbookSlotPacket>(OnSpellbookSlot);
        _listenersRegistered = true;

        UpdatePageButtons();
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<ExperienceBarPacket>(OnExperienceBar);
        GameManager.Instance.PacketManager.Remove<InventorySlotPacket>(OnInventorySlot);
        GameManager.Instance.PacketManager.Remove<ClearInventorySlotPacket>(OnClearInventorySlot);
        GameManager.Instance.PacketManager.Remove<SpellbookSlotPacket>(OnSpellbookSlot);
        _listenersRegistered = false;
    }

    private void LoadSlot(HotbarSlot slot, HotkeySetting setting)
    {
        if (setting == null || setting.SlotNumber == -1) return;

        if (setting.Type == HotkeySetting.SlotType.Item)
        {
            slot.SetItem(new ItemStats
            {
                SlotNumber = setting.SlotNumber,
                Name = "Loaded slot",
                GraphicId = 3002,
                GraphicFile = 13,
                GraphicR = 1
            });
        }
        else
        {
            slot.SetSpell(new SpellInfo
            {
                SlotNumber = setting.SlotNumber,
                Name = "Loaded slot",
                GraphicId = 3002,
                GraphicFile = 13
            });
        }
    }

    private void SaveSlots()
    {
        var settings = GameManager.Instance.CharacterSettings.Hotkeys;

        for (int p = 0; p < _pages.Length; p++)
        {
            var slots = _pages[p].Slots;

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];

                HotkeySetting setting;
                if (slot.ItemSlotIndex != -1)
                    setting = new(slot.ItemSlotIndex, HotkeySetting.SlotType.Item);
                else if (slot.SpellSlotIndex != -1)
                    setting = new(slot.SpellSlotIndex, HotkeySetting.SlotType.Spell);
                else
                    setting = new(-1, HotkeySetting.SlotType.Item);

                settings[p * SlotsPerPage + i] = setting;
            }
        }

        GameManager.Instance.CharacterSettings.Save();
    }

    public void SaveSlotsDelayed()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SetMount(string mount)
    {
        if (GameManager.Instance.CharacterSettings.MountName == mount) return;
        GameManager.Instance.CharacterSettings.MountName = mount;
        GameManager.Instance.CharacterSettings.Save();
    }

    private void OnExperienceBar(object o)
    {
        var p = (ExperienceBarPacket)o;

        // Server-provided fill fraction (GetInt32()/100 => 0..1), matching the Unity client.
        _xpBar.Value = p.Percentage;
        _xpText.Text = p.ExperienceToNextLevel.ToString("N0");

        if (p.Percentage == 1 && p.Experience == p.ExperienceToNextLevel)
        {
            _xpTooltip = $"Experience: {p.Experience:N0} / Sold: {p.ExperienceSold:N0}";
        }
        else
        {
            _xpTooltip = $"Experience: {p.Experience:N0} / {p.Experience + p.ExperienceToNextLevel:N0}";
        }
    }

    private void OnInventorySlot(object o)
    {
        var p = (InventorySlotPacket)o;
        var stats = ItemStats.FromPacket(p);

        foreach (var page in _pages)
        {
            foreach (var slot in page.Slots)
            {
                slot.OnInventorySlot(stats);
            }
        }

        if (stats.SlotNumber < 30)
        {
            if (stats.SlotType == ItemSlotType.Mount)
                _mountSlots[stats.SlotNumber] = stats.Name;
            else
                _mountSlots.Remove(stats.SlotNumber);
        }

        if (stats.SlotNumber == MountSlotNumber)
        {
            _mounted = true;
            SetMount(stats.Name);
        }
    }

    private void OnClearInventorySlot(object o)
    {
        var p = (ClearInventorySlotPacket)o;

        foreach (var page in _pages)
        {
            foreach (var slot in page.Slots)
            {
                slot.OnClearInventorySlot(p.SlotNumber);
            }
        }

        _mountSlots.Remove(p.SlotNumber);

        if (p.SlotNumber == MountSlotNumber)
            _mounted = false;
    }

    private void OnSpellbookSlot(object o)
    {
        var p = (SpellbookSlotPacket)o;
        var info = SpellInfo.FromPacket(p);

        foreach (var page in _pages)
        {
            foreach (var slot in page.Slots)
            {
                slot.OnSpellbookSlot(info);
            }
        }
    }

    private void UseSlot(int slotNumber)
    {
        if (GameManager.Instance.IsTargeting) return;

        var slot = _pages[_pageIndex].Slots[slotNumber];
        if (!slot.CanUse()) return;

        if (slot.ItemStats != null)
            InventoryWindow?.UseItem(slot.ItemStats);
        else if (slot.SpellInfo != null)
            SpellbookWindow?.UseSpell(slot.SpellInfo);
    }

    public void ToggleMount()
    {
        if (_mounted)
        {
            GameManager.Instance.NetworkClient.UseItem(MountSlotNumber);
        }
        else if (_mountSlots.Count > 0)
        {
            var mount = _mountSlots.FirstOrDefault(m => m.Value == GameManager.Instance.CharacterSettings.MountName);
            if (mount.Value == null)
                mount = _mountSlots.First();

            GameManager.Instance.NetworkClient.UseItem(mount.Key);
        }
    }

    public void OnBackClicked()
    {
        ChangePage(Math.Max(0, _pageIndex - 1));
    }

    public void OnNextClicked()
    {
        ChangePage((_pageIndex + 1) % PageCount);
    }

    public void CyclePage() => OnNextClicked();

    private void ChangePage(int newIndex)
    {
        if (_pageIndex == newIndex) return;
        _pages[_pageIndex].Container.Visible = false;
        _pages[newIndex].Container.Visible = true;
        _pageIndex = newIndex;
        UpdatePageButtons();
    }

    private void UpdatePageButtons()
    {
        _backButton.Visible = _pageIndex != 0;
        _nextButton.Visible = _pageIndex != PageCount - 1;
    }

    public override void _Process(double delta)
    {
        _repeatTimer += (float)delta;
        if (_repeatTimer < 0.1f) return;
        _repeatTimer = 0;

        // Guard against typing in input fields
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return;

        for (int i = 0; i < SlotsPerPage; i++)
        {
            string action = i == 9 ? "Hotkey0" : $"Hotkey{i + 1}";
            if (Input.IsActionPressed(action))
                UseSlot(i);
        }
    }
}
