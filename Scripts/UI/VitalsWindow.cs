using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;
using System.Collections.Generic;

namespace Goose2Client.UI
{
    /// <summary>
    /// Display-only HUD overlay showing HP/MP/SP bars, HP/MP/Level text,
    /// and per-bar text tooltips. Port of Unity VitalsWindow.
    /// The SP bar is gated on the first SNF, the ShowSpiritBar option (default
    /// on), and a per-character latch that persists once SP has ever been
    /// non-zero (SpiritBarVisibility).
    /// </summary>
    public partial class VitalsWindow : Control, IScalableWindow
    {
        private TextureProgressBar _hpBar;
        private TextureProgressBar _mpBar;
        private TextureProgressBar _spBar;
        private Label _hpText;
        private Label _mpText;
        private Label _spText;
        private Label _levelText;
        private Control _spOutline;

        private string _hpTooltip = "";
        private string _mpTooltip = "";
        private string _spTooltip = "";
        private string _levelTooltip = "";

        private bool _spLatch;
        private bool _snfReceived;
        private long _lastMaxSp;

        private List<UiScaleLayout.GeomRecord> _geom = null!;
        private VitalsCharacterDisplay _portrait;

        public override void _Ready()
        {
            _hpBar = GetNode<TextureProgressBar>("HpBar");
            _mpBar = GetNode<TextureProgressBar>("MpBar");
            _spBar = GetNode<TextureProgressBar>("SpBar");
            _hpText = GetNode<Label>("HpText");
            _mpText = GetNode<Label>("MpText");
            _spText = GetNode<Label>("SpText");
            _spOutline = GetNode<Control>("SpOutline");
            _levelText = GetNode<Label>("LevelText");
            _portrait = GetNode<VitalsCharacterDisplay>("Portrait");

            _hpBar.MaxValue = 1;
            _mpBar.MaxValue = 1;
            _spBar.MaxValue = 1;

            // Per-character latch: once SP has ever been non-zero, the bar stays
            // shown across relogins. Created at login, before the HUD exists, so
            // no null guard needed.
            _spLatch = GameManager.Instance.CharacterSettings
                .GetOption<bool>(Options.SpiritBarShown, false);

            // Connect hover handlers for tooltips. The value labels overlap the bars, so wire
            // both the bar AND its label (mouse_filter=Pass) — otherwise hovering the number
            // (which covers most of the bar) wouldn't surface the tooltip.
            _hpBar.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_hpTooltip, _hpBar);
            _hpBar.MouseExited += () => TooltipManager.Instance.HideTextTooltip();
            _hpText.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_hpTooltip, _hpText);
            _hpText.MouseExited += () => TooltipManager.Instance.HideTextTooltip();

            _mpBar.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_mpTooltip, _mpBar);
            _mpBar.MouseExited += () => TooltipManager.Instance.HideTextTooltip();
            _mpText.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_mpTooltip, _mpText);
            _mpText.MouseExited += () => TooltipManager.Instance.HideTextTooltip();

            _spBar.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_spTooltip, _spBar);
            _spBar.MouseExited += () => TooltipManager.Instance.HideTextTooltip();
            _spText.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_spTooltip, _spText);
            _spText.MouseExited += () => TooltipManager.Instance.HideTextTooltip();

            // Defensive: the scene already sets visible=false, but the pre-first-SNF
            // hidden state must hold even if a latched character logs in.
            _spBar.Visible = _spText.Visible = _spOutline.Visible = false;

            _levelText.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_levelTooltip, _levelText);
            _levelText.MouseExited += () => TooltipManager.Instance.HideTextTooltip();

            GameManager.Instance.PacketManager.Listen<StatusInfoPacket>(OnStatusInfo);

            var applier = UiScaleApplier.Instance;
            _geom = UiScaleLayout.Snapshot(this);
            applier.RegisterWindow(this);
            Relayout();
            TreeExited += () => applier.UnregisterWindow(this);
        }

        public void Relayout()
        {
            UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);
            _portrait.Relayout();
        }

        public override void _Process(double delta)
        {
            // Read-on-demand each frame (same pattern as SpellTargetManager for
            // target filtering) so the Options toggle takes effect immediately.
            bool optionOn = GameManager.Instance.CharacterSettings
                .GetOption<bool>(Options.ShowSpiritBar, true);
            ApplySpVisibility(optionOn);
        }

        private void ApplySpVisibility(bool optionOn)
        {
            bool show = SpiritBarVisibility.ShouldShow(_snfReceived, optionOn, _spLatch, _lastMaxSp);
            if (show != _spBar.Visible)
            {
                _spBar.Visible = show;
                _spText.Visible = show;
                _spOutline.Visible = show;
            }
        }

        public override void _ExitTree()
        {
            GameManager.Instance.PacketManager.Remove<StatusInfoPacket>(OnStatusInfo);
        }

        private void OnStatusInfo(object packetObj)
        {
            var p = (StatusInfoPacket)packetObj;

            _hpBar.Value = p.MaxHP == 0 ? 0 : p.CurrentHP / (double)p.MaxHP;
            _hpText.Text = p.CurrentHP.ToString("N0");
            _hpTooltip = $"Health: {p.CurrentHP:N0} / {p.MaxHP:N0}";

            _mpBar.Value = p.MaxMP == 0 ? 0 : p.CurrentMP / (double)p.MaxMP;
            _mpText.Text = p.CurrentMP.ToString("N0");
            _mpTooltip = $"Mana: {p.CurrentMP:N0} / {p.MaxMP:N0}";

            _snfReceived = true;
            _lastMaxSp = p.MaxSP;
            _spBar.Value = p.MaxSP == 0 ? 0 : p.CurrentSP / (double)p.MaxSP;
            _spText.Text = p.CurrentSP.ToString("N0");
            _spTooltip = $"Spirit: {p.CurrentSP:N0} / {p.MaxSP:N0}";

            // Persist the latch exactly once per false→true flip.
            if (p.MaxSP > 0 && !_spLatch)
            {
                _spLatch = true;
                var cs = GameManager.Instance.CharacterSettings;
                cs.Options[Options.SpiritBarShown] = true;
                cs.Save();
            }

            _levelText.Text = p.Level.ToString();
            _levelTooltip = $"Level: {p.Level}";
        }
    }
}
