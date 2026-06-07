using Godot;
using System;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Spellbook window — 8 pages × 30 slots = 240 slots, with paging, drag-drop reordering,
/// double-click cast, and network sync via SpellbookSlotPacket.
/// </summary>
public partial class SpellbookWindow : BaseWindow, IWindow
{
    public const int PageCount = 8;

    private int SlotsPerPage => Constants.SpellbookSlotsPerPage;

    private SpellbookPage[] _pages;
    private int _pageIndex;
    private SpellbookButton _backButton;
    private SpellbookButton _nextButton;
    private bool _listenersRegistered;

    private static readonly PackedScene SlotScene = GD.Load<PackedScene>("res://Scenes/UI/SpellSlot.tscn");

    public int WindowId => (int)WindowFrame;
    public WindowFrames WindowFrame => WindowFrames.Spellbook;

    public override void _Ready()
    {
        base._Ready();

        var pagesContainer = GetNode<Control>("Content/Pages");
        _backButton = GetNode<SpellbookButton>("Content/BackButton");
        _nextButton = GetNode<SpellbookButton>("Content/NextButton");
        _backButton.IsNext = false;
        _nextButton.IsNext = true;
        _backButton.Window = this;
        _nextButton.Window = this;

        _pages = new SpellbookPage[PageCount];
        for (int p = 0; p < PageCount; p++)
        {
            var grid = new GridContainer { Columns = 5 };
            grid.AddThemeConstantOverride("h_separation", 1);
            grid.AddThemeConstantOverride("v_separation", 1);
            grid.Name = $"Page{p}";
            pagesContainer.AddChild(grid);

            var slots = new SpellSlot[SlotsPerPage];
            for (int j = 0; j < SlotsPerPage; j++)
            {
                var slot = SlotScene.Instantiate<SpellSlot>();
                grid.AddChild(slot);
                slot.SlotNumber = p * SlotsPerPage + j;
                slot.Window = this;
                slot.OnDoubleClick = UseSpell;
                slot.OnMoveSpell = MoveSpell;
                slots[j] = slot;
            }

            _pages[p] = new SpellbookPage
            {
                StartSlotIndex = p * SlotsPerPage,
                Slots = slots,
                Container = grid
            };

            grid.Visible = (p == _pageIndex);
        }

        GameManager.Instance.PacketManager.Listen<SpellbookSlotPacket>(OnSpellbookSlot);
        _listenersRegistered = true;

        UpdatePageButtons();

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
        GameManager.Instance.PacketManager.Remove<SpellbookSlotPacket>(OnSpellbookSlot);
    }

    private SpellSlot GetSlot(int globalIndex)
    {
        var loc = SpellbookPaging.Locate(globalIndex, SlotsPerPage, PageCount);
        if (loc == null) return null;
        return _pages[loc.Value.Page].Slots[loc.Value.Slot];
    }

    private void OnSpellbookSlot(object o)
    {
        var p = (SpellbookSlotPacket)o;
        var slot = GetSlot(p.SlotNumber);
        if (slot != null)
            slot.SetSpell(SpellInfo.FromPacket(p));
    }

    public void UseSpell(SpellInfo info)
    {
        var lp = GameManager.Instance.CurrentMapManager?.LocalPlayer;
        if (GameManager.Instance.IsTargeting || (lp != null && lp.IsMounted)) return;

        if (info.TargetType == SpellTargetType.None)
        {
            GameManager.Instance.SpellCooldownManager.Cast(info.SlotNumber);
            GameManager.Instance.NetworkClient.CastSpell(
                info.SlotNumber,
                lp?.LoginId ?? GameManager.Instance.CurrentMapManager?.MyLoginId ?? 0);
        }
        else
        {
            GameManager.Instance.SpellTargetManager?.Cast(info);
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

    /// <summary>
    /// Auto-place spell to the first empty slot on the next/previous pages.
    /// Uses SpellbookPaging.FirstEmpty which stops at the FIRST empty (improvement
    /// over Unity which lacked a break and would issue redundant moves).
    /// </summary>
    public void MoveSpell(int fromIndex, bool forward)
    {
        var target = SpellbookPaging.FirstEmpty(
            fromIndex, forward, SlotsPerPage, PageCount,
            g => GetSlot(g)?.HasSpell ?? false);
        if (target.HasValue)
            MoveSpell(fromIndex, target.Value);
    }

    public void MoveSpell(int fromIndex, int toIndex)
    {
        GameManager.Instance.SpellCooldownManager.Swap(fromIndex, toIndex);
        GameManager.Instance.NetworkClient.MoveSpell(fromIndex, toIndex);
    }
}
