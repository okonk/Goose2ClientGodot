using Godot;
using System;

namespace Goose2Client.UI
{
    public partial class PartyEffect : Panel
    {
        public static readonly Vector2 IconSize = new(16, 16);

        private TextureRect _icon;
        private BuffSweepBar _sweep;
        private string _effectName;
        private string _tooltipText;
        private long _totalMs;
        private long _expiresAtMs;
        private bool _permanent;
        private bool _hovering;

        internal static string BuildTooltip(string name, double remainingSeconds)
        {
            var rs = TimeSpan.FromSeconds(Math.Max(0.0, remainingSeconds)).FormatDuration();
            if (rs.Length == 0)
                return name;
            return $"{name} ({rs} remaining)";
        }

        public override void _Ready()
        {
            CustomMinimumSize = IconSize;
            _icon = GetNode<TextureRect>("Icon");
            _sweep = GetNode<BuffSweepBar>("Sweep");
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;

            MouseEntered += OnMouseEntered;
            MouseExited += OnMouseExited;
        }

        public void Apply(PartyMemberEffect effect)
        {
            _effectName = effect.Name;
            _totalMs = effect.TotalMs;
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _permanent = effect.IsPermanent;
            _expiresAtMs = effect.ExpiresAt;
            _sweep.Visible = !_permanent;
            _tooltipText = _permanent ? _effectName : BuildTooltip(_effectName, (_expiresAtMs - nowMs) / 1000.0);
            Icon.Apply(_icon, effect.GraphicFile, effect.GraphicId, 0, 0, 0, 0);
            MouseFilter = MouseFilterEnum.Stop;
            Visible = true;
        }

        public void Clear()
        {
            _effectName = null;
            _tooltipText = null;
            _totalMs = 0;
            _expiresAtMs = 0;
            _permanent = false;
            _sweep.Visible = false;
            Icon.Clear(_icon);
            if (_hovering)
                TooltipManager.Instance?.HideTextTooltip();
            _hovering = false;
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
        }

        public override void _Process(double delta)
        {
            if (_effectName == null || _permanent)
                return;

            var remaining = (_expiresAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000.0;
            _sweep.Update(remaining, _totalMs / 1000.0, dangerSeconds: 10);

            if (_hovering && TooltipManager.Instance != null)
            {
                var text = BuildTooltip(_effectName, Mathf.Max(0.0, remaining));
                if (text != _tooltipText)
                {
                    _tooltipText = text;
                    TooltipManager.Instance.ShowTextTooltip(text, this);
                }
            }
        }

        private void OnMouseEntered()
        {
            _hovering = true;
            if (_tooltipText == null || TooltipManager.Instance == null)
                return;
            TooltipManager.Instance.ShowTextTooltip(_tooltipText, this);
        }

        private void OnMouseExited()
        {
            _hovering = false;
            TooltipManager.Instance?.HideTextTooltip();
        }
    }
}
