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
            // Labels are sized to their actual text height so there is no leftover row padding.
            var m = TooltipMetrics.MapItemMetrics(UiScaleApplier.Instance.Factor);

            Vector2 nameMin = _nameLabel.GetCombinedMinimumSize();
            _nameLabel.Position = new Vector2(m.LeftMargin, m.TopMargin);
            _nameLabel.Size = new Vector2(m.LabelWidth, nameMin.Y);

            float width = nameMin.X;
            float height = m.TopMargin + nameMin.Y + m.BottomMargin;

            if (_bindLabel.Visible)
            {
                Vector2 bindMin = _bindLabel.GetCombinedMinimumSize();
                _bindLabel.Position = new Vector2(m.LeftMargin, m.TopMargin + nameMin.Y + m.RowGap);
                _bindLabel.Size = new Vector2(m.LabelWidth, bindMin.Y);
                width = Mathf.Max(width, bindMin.X);
                height += m.RowGap + bindMin.Y;
            }

            // LeftMargin on the left, same on the right.
            Size = new Vector2(width + m.LeftMargin * 2f, height);

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
