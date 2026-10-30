using System;
using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class GridContainerWindow : BaseMultipleWindow
{
    private static readonly PackedScene SlotScene = GD.Load<PackedScene>("res://Scenes/UI/ItemSlot.tscn");

    private ItemSlot[] _slots = Array.Empty<ItemSlot>();
    private int _columns;

    public override WindowFrames WindowFrame => WindowFrames.GridContainer;

    protected override int LineCount => 0;

    public override void OnMakeWindow(MakeWindowPacket packet)
    {
        base.OnMakeWindow(packet);
        var (slots, cols) = GridContainerMetrics.Sanitize(packet.FrameParam1, packet.FrameParam2);
        if (_slots.Length != slots || _columns != cols)
            RebuildSlots(slots, cols);
        else
            for (int i = 0; i < _slots.Length; i++)
                _slots[i].ClearItem();
    }

    private void RebuildSlots(int slots, int columns)
    {
        for (int i = 0; i < _slots.Length; i++)
            _slots[i].QueueFree();
        _slots = new ItemSlot[slots];
        _columns = columns;
        var content = GetNode<Control>("Content");
        for (int i = 0; i < slots; i++)
        {
            var slot = SlotScene.Instantiate<ItemSlot>();
            // Slot geometry is owned by GridContainerMetrics + Relayout; the generic snapshot
            // must not capture it (a snapshot record would double-scale already-scaled offsets).
            slot.SetMeta(UiScaleLayout.SkipMeta, true);
            content.AddChild(slot);
            slot.SlotNumber = i;
            slot.Window = this;
            slot.OnDropItem = DropItem;
            _slots[i] = slot;
        }
    }

    public void SetSlot(int slotNumber, ItemStats stats)
    {
        if (slotNumber < 0 || slotNumber >= _slots.Length) return;
        _slots[slotNumber].SetItem(stats);
    }

    public void ClearSlot(int slotNumber)
    {
        if (slotNumber < 0 || slotNumber >= _slots.Length) return;
        _slots[slotNumber].ClearItem();
    }

    public override void Relayout()
    {
        base.Relayout();
        var factor = UiScaleApplier.Instance!.Factor;
        var slotSize = UiScale.ScaleSize(GridContainerMetrics.SlotSize, factor);
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i].Position = GridContainerMetrics.SlotPosition(i, _columns, factor);
            _slots[i].Size = new Vector2(slotSize, slotSize);
        }
        Size = GridContainerMetrics.WindowSize(_slots.Length, _columns, factor, AnyBottomButtonVisible());

        var margin = UiScale.ScaleSize(1f, factor);
        var titleBar = GetNode<Control>("TitleBar");
        titleBar.Size = new Vector2(Size.X, titleBar.Size.Y);
        var close = GetNode<Button>("TitleBar/CloseButton");
        close.Position = new Vector2(Size.X - close.Size.X - margin, close.Position.Y);

        PlaceBottomButtons(factor, margin);
    }

    private bool AnyBottomButtonVisible()
        => _backButton.Visible || _nextButton.Visible || _closeButton.Visible
            || (_okButton != null && _okButton.Visible);

    private void PlaceBottomButtons(float factor, float margin)
    {
        var size = GridContainerMetrics.BottomButtonSize(factor);
        var y = GridContainerMetrics.BottomButtonY(Size.Y, factor);
        foreach (var b in new[] { _backButton, _nextButton, _okButton, _closeButton })
        {
            if (b == null || !b.Visible) continue;
            var p = b.Position;
            b.Position = new Vector2(p.X, y);
            b.Size = size;
        }
        if (_closeButton.Visible)
            _closeButton.Position = new Vector2(Size.X - _closeButton.Size.X - margin, _closeButton.Position.Y);
    }

    internal override void OnEndWindow()
    {
        base.OnEndWindow();
        // The snapshot apply inside Relayout resets the root rect to the tscn rect, which
        // drops the manager-set position; restore it afterwards.
        var pos = Position;
        Relayout();
        Position = pos;
    }

    private void DropItem(IWindow fromWindow, int fromSlot, int toSlot)
    {
        if (fromWindow.WindowFrame == WindowFrames.Inventory)
        {
            GameManager.Instance.NetworkClient.MoveInventoryToWindow(fromSlot, WindowId, toSlot);
        }
        else if (fromWindow.WindowFrame != WindowFrames.Vendor)
        {
            GameManager.Instance.NetworkClient.MoveWindowToWindow(fromWindow.WindowId, fromSlot, WindowId, toSlot);
        }
    }

    protected override bool AcceptsItemDrops => true;

    protected override void OnItemDropOnWindow(ItemSlot source, Vector2 globalPosition)
    {
        int toSlot = NearestSlot(_slots, globalPosition);
        if (toSlot >= 0)
            DropItem(source.Window, source.SlotNumber, toSlot);
    }
}
