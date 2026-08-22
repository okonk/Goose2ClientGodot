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
    private CheckBox _scaleAuto;
    private CheckBox _scaleManual;
    private HSlider _scaleSlider;
    private Label _scaleValueLabel;
    private ButtonGroup _scaleModeGroup;
    private bool _dragging;
    private bool _initializing;

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

        _initializing = true;
        _scaleAuto = GetNode<CheckBox>("Content/ScaleAutoCheck");
        _scaleManual = GetNode<CheckBox>("Content/ScaleManualCheck");
        _scaleSlider = GetNode<HSlider>("Content/ScaleSlider");
        _scaleValueLabel = GetNode<Label>("Content/ScaleValueLabel");

        _scaleModeGroup = new ButtonGroup { AllowUnpress = false };
        _scaleAuto.ButtonGroup = _scaleModeGroup;
        _scaleManual.ButtonGroup = _scaleModeGroup;

        var cs = GameManager.Instance.CharacterSettings;
        var mode = UiScale.NormalizeMode(cs.GetOption<int>(Options.UiScaleMode, (int)UiScaleMode.Auto));
        var value = cs.GetOption<float>(Options.UiScaleValue, 1f);

        _scaleAuto.ButtonPressed = mode == UiScaleMode.Auto;
        _scaleManual.ButtonPressed = mode == UiScaleMode.Manual;
        _scaleSlider.Value = value;
        _scaleSlider.Visible = mode == UiScaleMode.Manual;
        RefreshScaleLabel();

        _scaleAuto.Toggled += OnScaleModeToggled;
        _scaleManual.Toggled += OnScaleModeToggled;
        _scaleSlider.DragStarted += () => _dragging = true;
        _scaleSlider.DragEnded += OnScaleDragEnded;
        _scaleSlider.ValueChanged += OnScaleValueChanged;
        // Synchronous clear, not a next-frame await: a deferred clear would race the
        // deferred ScaleRegister and ready-flush ordering.
        _initializing = false;

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

    private void OnScaleModeToggled(bool pressed)
    {
        if (!pressed || _initializing)
            return;
        var applier = UiScaleApplier.Instance;
        if (_scaleAuto.ButtonPressed)
        {
            CommitAuto();
            _scaleSlider.Visible = false;
        }
        else
        {
            applier.Mode = UiScaleMode.Manual;
            var cs = GameManager.Instance.CharacterSettings;
            cs.Options[Options.UiScaleMode] = (int)UiScaleMode.Manual;
            cs.Save();
            _scaleSlider.Visible = true;
            CommitManualValue((float)_scaleSlider.Value);
        }
        RefreshScaleLabel();
    }

    private void OnScaleDragEnded(bool valueChanged)
    {
        _dragging = false;
        CommitManualValue((float)_scaleSlider.Value);
    }

    private void OnScaleValueChanged(double v)
    {
        if (_initializing)
            return;
        RefreshScaleLabel();
        if (!_dragging)
            CommitManualValue((float)v);
    }

    private void CommitManualValue(float v)
    {
        float snapped = UiScale.NormalizeFactor(v);
        var cs = GameManager.Instance.CharacterSettings;
        cs.Options[Options.UiScaleValue] = snapped;
        cs.Save();
        var applier = UiScaleApplier.Instance;
        if (snapped != applier.Factor)
            applier.Apply(snapped, ApplyReason.UserCommit);
        RefreshScaleLabel();
    }

    private void CommitAuto()
    {
        // Never writes UiScaleValue: the dormant manual slider choice must survive an Auto excursion.
        var applier = UiScaleApplier.Instance;
        applier.Mode = UiScaleMode.Auto;
        var cs = GameManager.Instance.CharacterSettings;
        cs.Options[Options.UiScaleMode] = (int)UiScaleMode.Auto;
        cs.Save();
        int canvasY = (int)GetTree().Root.GetVisibleRect().Size.Y;
        applier.Apply(UiScale.AutoFactor(canvasY), ApplyReason.UserCommit);
    }

    private void RefreshScaleLabel()
    {
        var applier = UiScaleApplier.Instance;
        float f = applier.Mode == UiScaleMode.Manual
            ? (float)_scaleSlider.Value
            : applier.Factor;
        _scaleValueLabel.Text = applier.Mode == UiScaleMode.Manual
            ? FormatFactor(f) + "×"
            : "Auto (" + FormatFactor(f) + "×)";
    }

    private static string FormatFactor(float f)
        => (f % 1f == 0f) ? ((int)f).ToString() : f.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    public override void Relayout()
    {
        base.Relayout();
        RefreshScaleLabel();
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
