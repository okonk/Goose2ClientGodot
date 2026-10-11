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
                if (c.IsGM) _label.AddThemeColorOverride("font_color", GameColors.Blue);
                else _label.RemoveThemeColorOverride("font_color");
            }
        }

        public override void _Process(double delta)
        {
            // A window opened over a motionless cursor produces no input event, so
            // WorldViewport's gate never ran and the world hover is still live here.
            var show = _owner != null && GodotObject.IsInstanceValid(_owner)
                && Goose2Client.Character.NameDisplayRule.ShouldShowNameTooltip(
                       !_owner.IsHiddenFromViewer, _owner.IsRoofOccluded, _owner.ShouldShowNameOverhead)
                && !WindowOcclusion.IsPointCovered(GetGlobalMousePosition());
            if (!show) { Visible = false; return; }
            Visible = true;

            var m = TooltipMetrics.MapItemMetrics(UiScaleApplier.Instance!.Factor);
            Vector2 labelMin = _label.GetCombinedMinimumSize();
            _label.Position = new Vector2(m.LeftMargin, m.TopMargin);
            _label.Size = new Vector2(m.LabelWidth, labelMin.Y);
            Size = new Vector2(labelMin.X + m.LeftMargin * 2f, m.TopMargin + labelMin.Y + m.BottomMargin);

            var mouse = GetGlobalMousePosition();
            var vp = GetViewportRect().Size;
            float x = mouse.X - Size.X;
            if (x < 0) x = mouse.X;
            float y = mouse.Y;

            var item = TooltipManager.Instance?.MapItemTooltip;
            if (item != null && item.Visible)
            {
                y = item.GlobalPosition.Y - Size.Y - 2f;
                if (y < 0) y = item.GlobalPosition.Y + item.Size.Y + 2f;
            }

            if (y + Size.Y > vp.Y) y = vp.Y - Size.Y;
            GlobalPosition = new Vector2(x, y);
        }
    }
}
