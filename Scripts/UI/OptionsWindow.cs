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
    private CheckBox _showSpiritBar;
    private CheckBox _nativeRender;

    public override void _Ready()
    {
        base._Ready();

        // Opened from the Toolbar — hidden until toggled.
        Visible = false;

        _targetFiltering = GetNode<CheckBox>("Content/TargetFilteringCheck");
        _targetFiltering.ButtonPressed = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.TargetFiltering, true);
        _targetFiltering.Toggled += OnTargetFilteringChanged;

        _showSpiritBar = GetNode<CheckBox>("Content/ShowSpiritBarCheck");
        _showSpiritBar.ButtonPressed = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.ShowSpiritBar, true);
        _showSpiritBar.Toggled += OnShowSpiritBarChanged;

        _nativeRender = GetNode<CheckBox>("Content/NativeRenderCheck");
        _nativeRender.ButtonPressed = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.RenderMode, false);
        _nativeRender.Toggled += OnNativeRenderChanged;

        ScaleRegister();
    }

    private void OnTargetFilteringChanged(bool pressed)
    {
        GameManager.Instance.CharacterSettings.Options[Options.TargetFiltering] = pressed;
    }

    private void OnShowSpiritBarChanged(bool pressed)
    {
        GameManager.Instance.CharacterSettings.Options[Options.ShowSpiritBar] = pressed;
    }

    private void OnNativeRenderChanged(bool pressed)
    {
        GameManager.Instance.CharacterSettings.Options[Options.RenderMode] = pressed;
        GameManager.Instance.CharacterSettings.Save();
        GameManager.Instance.WorldViewport.ApplyMode(pressed ? WorldRenderMode.Native1x : WorldRenderMode.Integer2x);
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
