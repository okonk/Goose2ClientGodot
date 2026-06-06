using Godot;
using System;

namespace Goose2Client.UI
{
    /// <summary>
    /// Inventory item slot — displays an item icon + stack count,
    /// handles hover tooltips, double-click actions, and built-in Godot drag/drop.
    /// </summary>
    public partial class ItemSlot : Panel
    {
        private TextureRect _icon;
        private Label _count;

        public ItemStats Stats { get; private set; }
        public bool HasItem => Stats != null;
        public int StackSize => Stats?.StackSize ?? 0;

        public int SlotNumber { get; set; }
        public IWindow Window { get; set; }

        public Action<ItemStats> OnDoubleClick { get; set; }
        public Action<IWindow, int, int> OnDropItem { get; set; }

        public override void _Ready()
        {
            _icon = GetNode<TextureRect>("Icon");
            _count = GetNode<Label>("Count");

            MouseEntered += OnMouseEntered;
            MouseExited += OnMouseExited;
        }

        public void SetItem(ItemStats stats)
        {
            Stats = stats;
            Goose2Client.UI.Icon.Apply(_icon, stats.GraphicFile, stats.GraphicId, stats.GraphicR, stats.GraphicG, stats.GraphicB, stats.GraphicA);
            _count.Text = stats.StackSize.ToString();
            _count.Visible = stats.StackSize > 1;
        }

        public void ClearItem()
        {
            Stats = null;
            Goose2Client.UI.Icon.Clear(_icon);
            _count.Visible = false;
        }

        private void OnMouseEntered()
        {
            if (HasItem)
                TooltipManager.Instance.ShowItemTooltip(Stats, this);
        }

        private void OnMouseExited()
        {
            TooltipManager.Instance.HideItemTooltip();
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb &&
                mb.ButtonIndex == MouseButton.Left &&
                mb.DoubleClick &&
                HasItem)
            {
                OnDoubleClick?.Invoke(Stats);
            }
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (!HasItem)
                return default;

            var preview = new TextureRect
            {
                Texture = _icon.Texture,
                CustomMinimumSize = _icon.Size,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect
            };
            SetDragPreview(preview);

            return new Godot.Collections.Dictionary
            {
                { "kind", "item" },
                { "slot", this }
            };
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType != Variant.Type.Dictionary)
                return false;

            var d = data.AsGodotDictionary();
            return d.ContainsKey("kind") && d["kind"].AsString() == "item";
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var d = data.AsGodotDictionary();
            var src = d["slot"].As<ItemSlot>();
            if (src != null && src.HasItem)
                OnDropItem?.Invoke(src.Window, src.SlotNumber, SlotNumber);
        }
    }
}
