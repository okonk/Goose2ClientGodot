using Godot;
using System;

namespace Goose2Client.UI
{
    /// <summary>Spell tooltip: single label showing spell name + cooldown remaining.</summary>
    public partial class SpellTooltipControl : Control
    {
        private Label _label;

        private SpellInfo _spell;
        private Control _parent;
        private double _lastTooltipUpdate;

        public override void _Ready()
        {
            _label = GetNode<Label>("Label");
        }

        public void SetSpell(SpellInfo spell, Control parent)
        {
            _spell = spell;
            _parent = parent;
            _label.Text = GetSpellTooltipText();
            _lastTooltipUpdate = 0;
        }

        private string GetSpellTooltipText()
        {
            var remaining = GameManager.Instance.SpellCooldownManager.GetCooldownRemaining(_spell);
            string t = _spell.Name;
            if (remaining != TimeSpan.Zero)
            {
                var rs = remaining.FormatDuration();
                if (rs.Length > 0)
                    t += $" ({rs} remaining)";
            }
            return t;
        }

        public override void _Process(double delta)
        {
            if (_parent == null || !_parent.IsVisibleInTree())
            {
                Visible = false;
                return;
            }

            // Refresh cooldown text every 0.5s
            _lastTooltipUpdate += delta;
            if (_lastTooltipUpdate >= 0.5)
            {
                _label.Text = GetSpellTooltipText();
                _lastTooltipUpdate = 0;
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
