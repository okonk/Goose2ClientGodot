using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Goose2Client.UI
{
    /// <summary>Item tooltip: icon + name + type/flags + stat lines in a VBox.</summary>
    public partial class ItemTooltipControl : Control
    {
        private TextureRect _iconRect;
        private Label _nameLabel;
        private Label _typeLabel;
        private Label _flagsLabel;
        private VBoxContainer _statsVBox;

        private Control _parent;

        public override void _Ready()
        {
            _iconRect = GetNode<TextureRect>("Icon");
            _nameLabel = GetNode<Label>("Name");
            _typeLabel = GetNode<Label>("Type");
            _flagsLabel = GetNode<Label>("Flags");
            _statsVBox = GetNode<VBoxContainer>("StatsVBox");
        }

        public void SetItem(ItemStats stats, Control parent)
        {
            _parent = parent;

            Icon.Apply(_iconRect, stats.GraphicFile, stats.GraphicId, stats.GraphicR, stats.GraphicG, stats.GraphicB, stats.GraphicA);

            _nameLabel.Text = $"{stats.Title} {stats.Name} {stats.Surname}".Trim();
            _typeLabel.Text = ItemTooltipText.GetItemTypeText(stats);
            _flagsLabel.Text = ItemTooltipText.GetFlagsText(stats.Flags);

            // Clear existing stat lines
            foreach (Node child in _statsVBox.GetChildren())
                child.QueueFree();

            var lines = ItemTooltipText.Build(stats, id =>
                GameManager.Instance.Classes.TryGetValue(id, out var n) ? n : "");

            foreach (var (text, color) in lines)
            {
                var label = new Label();
                label.Text = text;
                label.AddThemeColorOverride("font_color", MapColor(color));
                label.Visible = text.Trim().Length > 0; // hide blank separator lines
                _statsVBox.AddChild(label);
            }
        }

        public override void _Process(double delta)
        {
            if (_parent == null || !_parent.IsVisibleInTree())
            {
                Visible = false;
                return;
            }

            PositionTooltip();
        }

        private void PositionTooltip()
        {
            var mouse = GetGlobalMousePosition();
            var size = Size;
            var vp = GetViewportRect().Size;

            float x = mouse.X - size.X;          // top-right pivot
            if (x < 0) x = mouse.X;              // overflow left -> top-left pivot
            float y = mouse.Y;                    // top edge at mouse
            if (y + size.Y > vp.Y) y = vp.Y - size.Y; // overflow bottom -> shift up

            GlobalPosition = new Vector2(x, y);
        }

        public static Color MapColor(ItemTooltipColor color)
        {
            return color switch
            {
                ItemTooltipColor.Description => new Color(1, 1, 1),
                ItemTooltipColor.AC => Color.Color8(216, 208, 176),
                ItemTooltipColor.WeaponDamage => Color.Color8(232, 244, 112),
                ItemTooltipColor.HpMp => Color.Color8(192, 204, 136),
                ItemTooltipColor.Stat => Color.Color8(136, 204, 192),
                ItemTooltipColor.Resistance => Color.Color8(208, 144, 144),
                ItemTooltipColor.Requirement => Color.Color8(232, 120, 112),
                ItemTooltipColor.Effect => Color.Color8(208, 168, 112),
                ItemTooltipColor.Value => Color.Color8(232, 224, 112),
                _ => new Color(1, 1, 1)
            };
        }
    }
}
