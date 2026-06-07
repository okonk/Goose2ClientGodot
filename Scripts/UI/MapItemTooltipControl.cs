using Godot;

namespace Goose2Client.UI
{
    /// <summary>Map item tooltip: name label + bind indicator label.</summary>
    public partial class MapItemTooltipControl : Control
    {
        private Label _nameLabel;
        private Label _bindLabel;

        public ItemStats Item { get; private set; }
        private Node2D _owner;

        public override void _Ready()
        {
            _nameLabel = GetNode<Label>("Name");
            _bindLabel = GetNode<Label>("Bind");
        }

        public void SetItem(ItemStats stats, Node2D owner)
        {
            Item = stats;
            _owner = owner;

            _nameLabel.Text = $"{stats.Title} {stats.Name} {stats.Surname}".Trim();
            if (stats.StackSize > 1)
                _nameLabel.Text += $" ({stats.StackSize})";

            _bindLabel.Visible = stats.Flags.HasFlag(ItemFlags.BindOnPickup);
        }

        public override void _Process(double delta)
        {
            if (_owner == null || !Godot.GodotObject.IsInstanceValid(_owner) || !_owner.Visible)
            {
                Visible = false;
                return;
            }

            // Size to content so the full-rect Background wraps the name (+ bind line when shown).
            // The Bind label sits at a fixed y=24, so the height is one or two fixed rows.
            float width = _nameLabel.GetCombinedMinimumSize().X;
            if (_bindLabel.Visible)
                width = Mathf.Max(width, _bindLabel.GetCombinedMinimumSize().X);
            float height = _bindLabel.Visible ? 40f : 24f;
            Size = new Vector2(width + 8f, height);

            PositionTooltip();
        }

        private void PositionTooltip()
        {
            var mouse = GetGlobalMousePosition();
            var size = Size;
            var vp = GetViewportRect().Size;

            float x = mouse.X - size.X;
            if (x < 0) x = mouse.X;
            float y = mouse.Y;
            if (y + size.Y > vp.Y) y = vp.Y - size.Y;

            GlobalPosition = new Vector2(x, y);
        }
    }
}
