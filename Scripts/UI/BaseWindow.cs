using Godot;
using Goose2Client;

namespace Goose2Client.UI;

/// <summary>
/// Base floating window with title-bar drag, hover transparency, and persisted position.
/// Replaces Unity TitleBar + WindowTransparency.
/// </summary>
public partial class BaseWindow : Control
{
    [Export] public string WindowName { get; set; }

    private Control _titleBar;
    private Button _closeButton;
    private bool _dragging;

    public override void _Ready()
    {
        _titleBar = GetNodeOrNull<Control>("TitleBar");
        _closeButton = GetNodeOrNull<Button>("TitleBar/CloseButton");

        // Restore persisted position
        if (WindowName != null)
        {
            var ws = GameManager.Instance.CharacterSettings.GetWindowSettings(WindowName);
            if (ws != null)
                Position = ws.Position;
        }

        // Title-bar drag
        if (_titleBar != null)
            _titleBar.GuiInput += OnTitleBarGuiInput;

        // Hover transparency (Unity WindowTransparency)
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
        Modulate = new Color(1, 1, 1, 0.7f);

        // Close button
        if (_closeButton != null)
            _closeButton.Pressed += OnClosePressed;
    }

    private void OnTitleBarGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _dragging = true;
            }
            else
            {
                _dragging = false;
                if (WindowName != null)
                    GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position);
            }
        }
        else if (@event is InputEventMouseMotion motion && _dragging)
        {
            Position += motion.Relative;
        }
    }

    private void OnMouseEntered() => Modulate = new Color(1, 1, 1, 1);

    private void OnMouseExited() => Modulate = new Color(1, 1, 1, 0.7f);

    public void Toggle() => Visible = !Visible;

    protected virtual void OnClosePressed() => Hide();
}
