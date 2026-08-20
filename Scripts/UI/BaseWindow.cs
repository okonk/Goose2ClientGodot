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
    private bool _hovered;

    private const float HoverOpacity = 1f;
    private const float UnhoveredOpacity = 0.7f;

    protected Label TitleLabel { get; private set; }
    protected Control Content { get; private set; }
    protected TextureRect Background { get; private set; }

    public string Title { set { if (TitleLabel != null) TitleLabel.Text = value; } }

    public override void _Ready()
    {
        _titleBar = GetNodeOrNull<Control>("TitleBar");
        _closeButton = GetNodeOrNull<Button>("TitleBar/CloseButton");
        TitleLabel = GetNodeOrNull<Label>("TitleBar/TitleLabel");
        Content = GetNodeOrNull<Control>("Content");
        Background = GetNodeOrNull<TextureRect>("Background");

        // The full-rect Content (MouseFilter=Pass) is drawn on top of the TitleBar and
        // swallows its clicks — Pass forwards unhandled events to the PARENT, never to the
        // TitleBar sibling — which kills title-bar dragging. Make Content transparent to the
        // mouse so the TitleBar receives drag clicks. Interactive descendants (slots, buttons,
        // bars) keep their own MouseFilter and are unaffected (mouse_filter does not cascade).
        if (Content != null)
            Content.MouseFilter = MouseFilterEnum.Ignore;

        // Restore persisted position (or first-run default). Positions were saved in the native
        // canvas of the window size they were saved on; legacy files (and the 1280x720 design
        // defaults) are LegacyCanvas. Edge-stick + clamp re-anchors onto the current canvas.
        if (WindowName != null)
        {
            var ws = GameManager.Instance.CharacterSettings.GetWindowSettings(WindowName);
            var currentCanvas = (Vector2I)GetTree().Root.GetVisibleRect().Size;
            if (ws == null && DefaultWindowLayout.IsDialog(WindowName))
            {
                // First-run transient dialog: open centered; a saved position (after a drag)
                // always goes through Resolve below, so the edge-stick rule takes over.
                Position = WindowPlacement.Center(currentCanvas, Size);
            }
            else
            {
                var storedOrDefaultPos = ws != null ? ws.Position : DefaultWindowLayout.For(WindowName);
                var savedCanvas = ws != null && ws.CanvasSize != default ? ws.CanvasSize : WindowPlacement.LegacyCanvas;
                Position = WindowPlacement.Resolve(storedOrDefaultPos, Size, savedCanvas, currentCanvas);
            }
            if (ws != null) Visible = ws.Visible;
        }

        // Title-bar drag
        if (_titleBar != null)
            MakeDragHandle(_titleBar);

        // Hover transparency (Unity WindowTransparency). The cursor position is
        // checked against the window rect in _Process instead of using this
        // control's own MouseEntered/MouseExited: the viewport tracks only the
        // topmost control under the cursor, so moving onto a slot (Panel,
        // MouseFilter.Stop) fires mouse_exited on THIS window and would fade it
        // to 70% even though the cursor is still on the window.
        Modulate = new Color(1, 1, 1, UnhoveredOpacity);

        // Close button
        if (_closeButton != null)
            _closeButton.Pressed += OnClosePressed;

        // Keep the title bar (and its CloseButton) the topmost sibling so its drag region
        // and close button always receive clicks, even when a full-rect Content child
        // (e.g. CharacterWindow's SlotGrid) would otherwise occlude them. Sibling pick
        // order follows tree order; last child = drawn on top = picked first.
        if (_titleBar != null)
            MoveChild(_titleBar, GetChildCount() - 1);
    }

    /// <summary>Makes a control a drag handle for this window (e.g. the hotbar's XP bar).
    /// Handles must receive mouse input (not MouseFilter.Ignore) and be sized to the
    /// region the user can grab.</summary>
    protected void MakeDragHandle(Control handle)
        => handle.GuiInput += OnTitleBarGuiInput;

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
                    GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, Visible, (Vector2I)GetTree().Root.GetVisibleRect().Size);
            }
        }
        else if (@event is InputEventMouseMotion motion && _dragging)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _dragging = false;
                if (WindowName != null)
                    GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, Visible, (Vector2I)GetTree().Root.GetVisibleRect().Size);
                return;
            }
            Position += motion.Relative;
        }
    }

    public override void _Process(double delta)
    {
        bool inside = Visible && GetGlobalRect().HasPoint(GetGlobalMousePosition());
        if (inside != _hovered)
        {
            _hovered = inside;
            Modulate = new Color(1, 1, 1, inside ? HoverOpacity : UnhoveredOpacity);
        }
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (WindowName != null)
            GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, Visible, (Vector2I)GetTree().Root.GetVisibleRect().Size);
    }

    protected virtual void OnClosePressed()
    {
        Hide();
        if (WindowName != null)
            GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, false, (Vector2I)GetTree().Root.GetVisibleRect().Size);
    }
}
