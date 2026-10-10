using Godot;

namespace Goose2Client.UI
{
    // Fully-qualify Goose2Client.Character.Character: from this namespace the bare `Character`
    // resolves to the namespace, not the class (see BridgedNameLabel.cs:40).
    public partial class NameTooltipControl : Control
    {
        private Label _label = null!;
        private Goose2Client.Character.Character? _owner;

        public override void _Ready() => _label = GetNode<Label>("Label");

        public void SetCharacter(Goose2Client.Character.Character? c)
        {
            _owner = c;
            if (c != null)
            {
                _label.Text = c.FullName;
                _label.AddThemeColorOverride("font_color", c.IsGM ? GameColors.Blue : new Color(1, 1, 1));
            }
        }

        public override void _Process(double delta)
        {
            if (_owner == null || !GodotObject.IsInstanceValid(_owner)
                || !Goose2Client.Character.NameDisplayRule.ShouldShowNameTooltip(
                       !_owner.IsHiddenFromViewer, _owner.IsRoofOccluded, _owner.ShouldShowNameOverhead))
            {
                Visible = false;
                return;
            }

            var pad = TooltipMetrics.TextPad(UiScaleApplier.Instance!.Factor);
            Size = _label.GetCombinedMinimumSize() + new Vector2(pad.W, pad.H);
            _label.OffsetLeft = pad.W / 2;
            _label.OffsetTop = pad.H / 2;
            _label.OffsetRight = -pad.W / 2;
            _label.OffsetBottom = -pad.H / 2;

            var mouse = GetGlobalMousePosition();
            var vp = GetViewportRect().Size;
            float x = mouse.X - Size.X;
            if (x < 0) x = mouse.X;
            float y = mouse.Y;

            var item = TooltipManager.Instance?.MapItemTooltip;
            if (item != null && item.Visible)
                y = item.GlobalPosition.Y + item.Size.Y + 2f;

            if (y + Size.Y > vp.Y) y = vp.Y - Size.Y;
            GlobalPosition = new Vector2(x, y);
        }
    }
}
