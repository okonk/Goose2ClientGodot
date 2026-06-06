using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI
{
    /// <summary>
    /// Display-only HUD overlay showing HP/MP bars, HP/MP/Level text,
    /// and per-bar text tooltips. Port of Unity VitalsWindow.
    /// </summary>
    public partial class VitalsWindow : Control
    {
        private TextureProgressBar _hpBar;
        private TextureProgressBar _mpBar;
        private Label _hpText;
        private Label _mpText;
        private Label _levelText;

        private string _hpTooltip = "";
        private string _mpTooltip = "";
        private string _levelTooltip = "";

        public override void _Ready()
        {
            _hpBar = GetNode<TextureProgressBar>("HpBar");
            _mpBar = GetNode<TextureProgressBar>("MpBar");
            _hpText = GetNode<Label>("HpText");
            _mpText = GetNode<Label>("MpText");
            _levelText = GetNode<Label>("LevelText");

            _hpBar.MaxValue = 1;
            _mpBar.MaxValue = 1;

            // Connect hover handlers for tooltips
            _hpBar.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_hpTooltip, _hpBar);
            _hpBar.MouseExited += () => TooltipManager.Instance.HideTextTooltip();

            _mpBar.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_mpTooltip, _mpBar);
            _mpBar.MouseExited += () => TooltipManager.Instance.HideTextTooltip();

            _levelText.MouseEntered += () => TooltipManager.Instance.ShowTextTooltip(_levelTooltip, _levelText);
            _levelText.MouseExited += () => TooltipManager.Instance.HideTextTooltip();

            GameManager.Instance.PacketManager.Listen<StatusInfoPacket>(OnStatusInfo);
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

            _levelText.Text = p.Level.ToString();
            _levelTooltip = $"Level: {p.Level}";
        }
    }
}
