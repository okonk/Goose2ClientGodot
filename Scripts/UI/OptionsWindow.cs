using Godot;
using Goose2Client;

namespace Goose2Client.UI;

/// <summary>
/// Options window — target filtering toggle.
/// Hidden until opened from the Toolbar.
/// </summary>
public partial class OptionsWindow : BaseWindow
{
    private CheckBox _targetFiltering;

    public override void _Ready()
    {
        base._Ready();

        // Opened from the Toolbar — hidden until toggled.
        Visible = false;

        _targetFiltering = GetNode<CheckBox>("Content/TargetFilteringCheck");
        _targetFiltering.ButtonPressed = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.TargetFiltering, true);
        _targetFiltering.Toggled += OnTargetFilteringChanged;
    }

    private void OnTargetFilteringChanged(bool pressed)
    {
        GameManager.Instance.CharacterSettings.Options[Options.TargetFiltering] = pressed;
    }

    public void ToggleWindow()
    {
        Visible = !Visible;
        if (!Visible)
            GameManager.Instance.CharacterSettings.Save();
    }

    protected override void OnClosePressed()
    {
        Hide();
        GameManager.Instance.CharacterSettings.Save();
    }
}
