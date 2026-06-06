using Godot;

namespace Goose2Client.UI
{
    /// <summary>Simple text tooltip: single label.</summary>
    public partial class TextTooltipControl : Control
    {
        private Label _label;
        private Control _parent;

        public override void _Ready()
        {
            _label = GetNode<Label>("Label");
        }

        public void SetText(string text, Control parent)
        {
            _label.Text = text;
            _parent = parent;
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

            float x = mouse.X - size.X;
            if (x < 0) x = mouse.X;
            float y = mouse.Y;
            if (y + size.Y > vp.Y) y = vp.Y - size.Y;

            GlobalPosition = new Vector2(x, y);
        }
    }
}
