using System;
using Godot;
using Goose2Client;

namespace Goose2Client.UI;

/// <summary>
/// Options window — target filtering toggle.
/// Hidden until opened from the Toolbar.
/// </summary>
public partial class OptionsWindow : BaseWindow
{
    protected override bool DefaultVisible => false;

    private CheckBox _targetFiltering = null!;
    private CheckBox _showSpiritBar = null!;
    private HSlider _renderScaleSlider = null!;
    private Label _renderScaleValueLabel = null!;
    private bool _renderScaleDragging;
    private CheckBox _minimap = null!;
    private HSlider _minimapOpacitySlider = null!;
    private bool _minimapOpacityDragging;
    private CheckBox _lockHudWindows = null!;
    private Panel _targetColorSwatch = null!;
    private StyleBoxFlat _targetColorStyle = null!;
    private Control _targetColorPicker = null!;
    private ColorPickerControl _targetColorPickerControl = null!;
    private CheckBox _scaleAuto = null!;
    private CheckBox _scaleManual = null!;
    private HSlider _scaleSlider = null!;
    private Label _scaleValueLabel = null!;
    private ButtonGroup _scaleModeGroup = null!;
    private Button _resetLayoutButton = null!;
    private Button _keyBindingsButton = null!;
    private bool _dragging;
    private bool _initializing;

    public Action? OpenKeyBindings;

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

        _renderScaleSlider = GetNode<HSlider>("Content/RenderScaleSlider");
        _renderScaleValueLabel = GetNode<Label>("Content/RenderScaleValueLabel");
        _renderScaleSlider.Value = Mathf.Clamp(
            GameManager.Instance.CharacterSettings.GetOption<int>(Options.RenderScale, 2),
            (int)_renderScaleSlider.MinValue, (int)_renderScaleSlider.MaxValue);
        _renderScaleValueLabel.Text = (int)_renderScaleSlider.Value + "×";
        _renderScaleSlider.DragStarted += () => _renderScaleDragging = true;
        _renderScaleSlider.DragEnded += OnRenderScaleDragEnded;
        _renderScaleSlider.ValueChanged += OnRenderScaleValueChanged;

        var viewport = GameManager.Instance.WorldViewport;
        if (viewport != null)
        {
            viewport.ScaleChanged += OnViewportScaleChanged;
            viewport.GetWindow().SizeChanged += RefreshRenderScaleLabel;
        }
        RefreshRenderScaleLabel();

        _minimap = GetNode<CheckBox>("Content/MinimapCheck");
        _minimap.ButtonPressed = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.Minimap, true);
        _minimap.Toggled += OnMinimapChanged;

        _minimapOpacitySlider = GetNode<HSlider>("Content/MinimapOpacitySlider");
        _minimapOpacitySlider.Value = Mathf.Clamp(
            GameManager.Instance.CharacterSettings.GetOption<float>(Options.MinimapOpacity, 1f),
            _minimapOpacitySlider.MinValue, _minimapOpacitySlider.MaxValue);
        _minimapOpacitySlider.DragStarted += () => _minimapOpacityDragging = true;
        _minimapOpacitySlider.DragEnded += OnMinimapOpacityDragEnded;
        _minimapOpacitySlider.ValueChanged += OnMinimapOpacityChanged;

        _lockHudWindows = GetNode<CheckBox>("Content/LockHudWindowsCheck");
        _lockHudWindows.ButtonPressed = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.LockHudWindows, false);
        _lockHudWindows.Toggled += OnLockHudWindowsChanged;

        _targetColorSwatch = GetNode<Panel>("Content/TargetColorSwatch");
        _targetColorStyle = (StyleBoxFlat)_targetColorSwatch.GetThemeStylebox("panel").Duplicate();
        _targetColorSwatch.AddThemeStyleboxOverride("panel", _targetColorStyle);
        _targetColorSwatch.GuiInput += OnTargetColorSwatchGuiInput;

        _targetColorPicker = GetNode<Control>("Content/TargetColorPicker");
        _targetColorPickerControl = GetNode<ColorPickerControl>("Content/TargetColorPicker/Picker");
        _targetColorPickerControl.SetColor(TargetBoxColor.CurrentColor());
        _targetColorPickerControl.ColorChanged += OnTargetColorChanged;
        _targetColorStyle.BgColor = TargetBoxColor.CurrentColor();

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

        _resetLayoutButton = GetNode<Button>("Content/ResetLayoutButton");
        _resetLayoutButton.Pressed += OnResetLayoutPressed;

        _keyBindingsButton = GetNode<Button>("Content/KeyBindingsButton");
        _keyBindingsButton.Pressed += OnKeyBindingsPressed;
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

    private void OnRenderScaleValueChanged(double v)
    {
        int scale = (int)v;
        GameManager.Instance.WorldViewport.ApplyMode(scale);
        RefreshRenderScaleLabel();
        if (!_renderScaleDragging)
        {
            var cs = GameManager.Instance.CharacterSettings;
            cs.Options[Options.RenderScale] = scale;
            cs.Save();
        }
    }

    // Reports the scale the world actually renders at, not the requested one: a request the
    // window is too large for is lifted, and a label echoing the request would then name a zoom
    // the user is not looking at (a 2x request renders 3x at 3840x2160).
    private void RefreshRenderScaleLabel()
    {
        int requested = (int)_renderScaleSlider.Value;
        var viewport = GameManager.Instance.WorldViewport;
        int resolved = viewport != null ? viewport.ResolvedScale : requested;
        _renderScaleValueLabel.Text = resolved == requested
            ? requested + "×"
            : requested + "× → " + resolved + "×";
    }

    private void OnRenderScaleDragEnded(bool valueChanged)
    {
        _renderScaleDragging = false;
        RefreshRenderScaleLabel();
        var cs = GameManager.Instance.CharacterSettings;
        cs.Options[Options.RenderScale] = (int)_renderScaleSlider.Value;
        cs.Save();
    }

    private void OnMinimapChanged(bool pressed)
    {
        GameManager.Instance.CharacterSettings.Options[Options.Minimap] = pressed;
        GameManager.Instance.CharacterSettings.Save();
        GameManager.Instance.Hud?.Minimap?.SetEnabled(pressed);
    }

    private void OnMinimapOpacityChanged(double v)
    {
        float o = (float)v;
        var cs = GameManager.Instance.CharacterSettings;
        cs.Options[Options.MinimapOpacity] = o;
        GameManager.Instance.Hud?.Minimap?.SetOpacity(o);
        if (!_minimapOpacityDragging)
            cs.Save();
    }

    private void OnMinimapOpacityDragEnded(bool valueChanged)
    {
        _minimapOpacityDragging = false;
        GameManager.Instance.CharacterSettings.Save();
    }

    private void OnLockHudWindowsChanged(bool pressed)
    {
        GameManager.Instance.CharacterSettings.Options[Options.LockHudWindows] = pressed;
        GameManager.Instance.CharacterSettings.Save();
    }

    private void OnTargetColorSwatchGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            return;
        if (_targetColorPicker.Visible)
        {
            CloseTargetColorPicker();
            return;
        }

        _targetColorPicker.Visible = true;
        _targetColorPickerControl.SetColor(TargetBoxColor.CurrentColor());
        PositionTargetColorPicker();
        _targetColorPickerControl.SyncHsl();
    }

    private void OnTargetColorChanged(Color color)
    {
        GameManager.Instance.CharacterSettings.Options[Options.TargetBoxColor] = TargetBoxColor.Format(color);
        _targetColorStyle.BgColor = color;
        GameManager.Instance.SpellTargetManager?.RefreshReticleColor();
    }

    // Beside the swatch, flipping to its left when the canvas has no room on the right.
    private void PositionTargetColorPicker()
    {
        var origin = GlobalPosition;
        var swatch = _targetColorSwatch.GetGlobalRect();
        var size = _targetColorPicker.Size;
        var canvas = GetTree().Root.GetVisibleRect();
        float x = swatch.End.X - origin.X + 6f;
        if (swatch.End.X + 6f + size.X > canvas.End.X)
            x = swatch.Position.X - origin.X - size.X - 6f;
        float y = swatch.GetCenter().Y - origin.Y - size.Y / 2f;
        _targetColorPicker.Position = new Vector2(x, Mathf.Clamp(y, 4f - origin.Y, canvas.End.Y - origin.Y - size.Y - 4f));
    }

    private void CloseTargetColorPicker()
    {
        if (_targetColorPicker == null || !_targetColorPicker.Visible)
            return;
        _targetColorPicker.Visible = false;
        GameManager.Instance.CharacterSettings.Save();
    }

    // No popup node to own the click-away, so the panel closes itself on any press outside it.
    // The swatch is excluded: its own handler toggles, and closing here would reopen it.
    public override void _Input(InputEvent @event)
    {
        base._Input(@event);
        if (!Visible || _targetColorPicker == null || !_targetColorPicker.Visible)
            return;
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
            return;
        if (_targetColorPicker.GetGlobalRect().HasPoint(mb.GlobalPosition)
            || _targetColorSwatch.GetGlobalRect().HasPoint(mb.GlobalPosition))
            return;
        CloseTargetColorPicker();
        // Swallowed like a popup's click-away, so dismissing never reaches the world as a walk or cast.
        GetViewport().SetInputAsHandled();
    }

    protected override bool IsHoverPoint(Vector2 globalPoint)
        => base.IsHoverPoint(globalPoint)
           || (_targetColorPicker != null && _targetColorPicker.Visible
               && _targetColorPicker.GetGlobalRect().HasPoint(globalPoint));

    private void OnScaleModeToggled(bool pressed)
    {
        if (!pressed || _initializing)
            return;
        var applier = UiScaleApplier.Instance!;
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
        var applier = UiScaleApplier.Instance!;
        if (snapped != applier.Factor)
            applier.Apply(snapped, ApplyReason.UserCommit);
        RefreshScaleLabel();
    }

    private void CommitAuto()
    {
        // Never writes UiScaleValue: the dormant manual slider choice must survive an Auto excursion.
        var applier = UiScaleApplier.Instance!;
        applier.Mode = UiScaleMode.Auto;
        var cs = GameManager.Instance.CharacterSettings;
        cs.Options[Options.UiScaleMode] = (int)UiScaleMode.Auto;
        cs.Save();
        int canvasY = (int)GetTree().Root.GetVisibleRect().Size.Y;
        applier.Apply(UiScale.AutoFactor(canvasY), ApplyReason.UserCommit);
    }

    private void RefreshScaleLabel()
    {
        var applier = UiScaleApplier.Instance!;
        float f = applier.Mode == UiScaleMode.Manual
            ? (float)_scaleSlider.Value
            : applier.Factor;
        _scaleValueLabel.Text = applier.Mode == UiScaleMode.Manual
            ? FormatFactor(f) + "×"
            : "Auto (" + FormatFactor(f) + "×)";
    }

    private static string FormatFactor(float f)
        => (f % 1f == 0f) ? ((int)f).ToString() : f.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    // Reposition only: the options window is the control panel running the reset and must stay open.
    public override void ResetToDefault()
    {
        RepositionFromSaved();
    }

    private void OnKeyBindingsPressed()
    {
        OpenKeyBindings?.Invoke();
    }

    private void OnResetLayoutPressed()
    {
        GameManager.Instance.CharacterSettings.ResetWindowSettings();
        foreach (var w in GameManager.Instance.HudWindows())
            w.ResetToDefault();
        GameManager.Instance.Hud!.ResetMovableWindowPositions();
    }

    public override void Relayout()
    {
        base.Relayout();
        RefreshScaleLabel();
        RefreshRenderScaleLabel();
        if (_targetColorPicker == null)
            return;
        _targetColorPickerControl.SyncHsl();
        if (_targetColorPicker.Visible)
            PositionTargetColorPicker();
    }

    public void ToggleWindow()
    {
        CloseTargetColorPicker();
        Visible = !Visible;
        if (!Visible)
            GameManager.Instance.CharacterSettings.Save();
    }

    protected override void OnClosePressed()
    {
        CloseTargetColorPicker();
        Hide();
        GameManager.Instance.CharacterSettings.Save();
    }

    // WorldViewport outlives the HUD (ReturnToLogin frees the UI layer but keeps it), and
    // ScaleChanged is a plain C# event, so the subscription must be removed manually or the
    // old window's handler fires against its disposed nodes after a reconnect.
    public override void _ExitTree()
    {
        var viewport = GameManager.Instance?.WorldViewport;
        if (viewport != null)
            viewport.ScaleChanged -= OnViewportScaleChanged;
        base._ExitTree();
    }

    private void OnViewportScaleChanged(float _)
    {
        RefreshRenderScaleLabel();
    }
}
