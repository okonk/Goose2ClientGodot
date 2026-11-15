using Godot;
using Goose2Client.Character;

namespace Goose2Client.UI;

public partial class RespawnWindow : BaseWindow
{
    protected override bool DefaultVisible => false;

    private Label _prompt = null!;
    private Button _respawnButton = null!;
    private Character.Character? _player;

    public override void _Ready()
    {
        base._Ready();

        Visible = false;
        Title = "Dead";

        _prompt = GetNode<Label>("Content/Prompt");
        _respawnButton = GetNode<Button>("Content/RespawnButton");
        _respawnButton.Pressed += OnRespawnPressed;
        GetNode<Button>("TitleBar/CloseButton").Visible = false;

        UiScaleApplier.Instance!.ApplyFontSize(GetNode<Label>("TitleBar/TitleLabel"), 9);
        ScaleRegister();

        GameManager.Instance.CharacterUpdated += OnCharacterUpdated;
    }

    public override void _ExitTree()
    {
        if (GameManager.Instance != null) GameManager.Instance.CharacterUpdated -= OnCharacterUpdated;
    }

    private void OnCharacterUpdated(Character.Character c)
    {
        if (!c.IsLocalPlayer) return;

        if (!c.IsDead)
        {
            _player = null;
            Hide();
            return;
        }

        _player = c;
        _respawnButton.Disabled = false;
        Visible = true;
        Activate();
        UpdatePrompt();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (Visible) UpdatePrompt();
    }

    private void UpdatePrompt()
    {
        if (_player == null || !GodotObject.IsInstanceValid(_player)) return;
        int secs = _player.RespawnSecondsLeft;
        _prompt.Text = $"You have died. Respawning in {RespawnCountdown.Format(secs)}...";
        if (secs == 0) _respawnButton.Disabled = true;
    }

    private void OnRespawnPressed()
    {
        _respawnButton.Disabled = true;
        GameManager.Instance.NetworkClient.RequestRespawn();
    }

    protected override void OnClosePressed() { }
}
