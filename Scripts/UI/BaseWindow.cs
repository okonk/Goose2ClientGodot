using System.Collections.Generic;
using Godot;
using Goose2Client;

namespace Goose2Client.UI;

/// <summary>
/// Base floating window with title-bar drag, hover transparency, and persisted position.
/// Replaces Unity TitleBar + WindowTransparency.
/// </summary>
public partial class BaseWindow : Control, IScalableWindow
{
    [Export] public string WindowName { get; set; }

    private Control _titleBar;
    private Button _closeButton;
    private bool _dragging;
    private Vector2 _preDragPosition;
    private bool _dragCancelled;
    private bool _hovered;

    private List<UiScaleLayout.GeomRecord> _geom = null!;
    private bool _scaleRegistered;
    private Vector2 _tscnSize;

    private const float HoverOpacity = 1f;
    private const float UnhoveredOpacity = 0.7f;

    protected Label TitleLabel { get; private set; }
    protected Control Content { get; private set; }
    protected TextureRect Background { get; private set; }

    public string Title { set { if (TitleLabel != null) TitleLabel.Text = value; } }

    public override void _Ready()
    {
        // tscn size is the 1x base for placement math; relayout (below, via ScaleRegister)
        // resizes the frame after this capture.
        _tscnSize = Size;

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

        if (WindowName != null)
        {
            var ws = GameManager.Instance.CharacterSettings.GetWindowSettings(WindowName);
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

        // Deferred so subclass _Ready build code runs first; their synchronous ScaleRegister
        // calls make this a no-op (idempotent via _scaleRegistered).
        Callable.From(() => ScaleRegister()).CallDeferred();
    }

    // Single owner of placement+scale at registration: the snapshot (1x base) must precede
    // the first Relayout in the same frame, or it would capture already-scaled geometry.
    protected void ScaleRegister()
    {
        if (_scaleRegistered) return;
        _scaleRegistered = true;
        _geom = UiScaleLayout.Snapshot(this);
        var applier = UiScaleApplier.Instance;
        applier.RegisterWindow(this);
        Relayout();
        RepositionFromSaved();
        TreeExited += () => applier.UnregisterWindow(this);
    }

    public virtual void Relayout()
    {
        UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);
    }

    public void RepositionFromSaved()
    {
        if (!IsInsideTree()) return;
        var canvas = (Vector2I)GetTree().Root.GetVisibleRect().Size;
        var ws = GameManager.Instance?.CharacterSettings?.GetWindowSettings(WindowName);
        var placed = ws != null && ws.Placed;                       // (b) valid quad — Position may legitimately be (0,0)
        var legacy = !placed && ws != null && ws.Position != default; // (a) pre-feature position, honored with legacy size/factor
        if (!placed && !legacy && DefaultWindowLayout.IsDialog(WindowName))
        {
            Position = WindowPlacement.Center(canvas, Size);
            return;
        }
        var pos = placed || legacy ? ws.Position : DefaultWindowLayout.For(WindowName); // (c) unplaced non-dialog → default layout
        var savedCanvas = ws != null && ws.CanvasSize != default ? ws.CanvasSize : WindowPlacement.LegacyCanvas;
        var savedSize = placed && ws.Size == default ? (DefaultWindowLayout.LegacySize(WindowName) ?? _tscnSize)   // defensive: Placed is written with Size
            : (!placed ? (DefaultWindowLayout.LegacySize(WindowName) ?? _tscnSize) : ws.Size);
        var savedFactor = placed && ws.Factor > 0f ? ws.Factor : 1f;   // defensive: Placed is written with Factor
        var applier = UiScaleApplier.Instance;
        Position = WindowPlacement.ResolveScaled(pos, savedSize, savedFactor, savedCanvas, Size,
            applier != null ? applier.Factor : 1f, canvas,
            applier != null ? applier.ScaleSize(24f) : WindowPlacement.TitleBarHeight);
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
                // Also cleared here: a cancelled release with the cursor off the title bar never
                // reaches a guarded release, and the flag would otherwise swallow the next save.
                if (_dragCancelled) _dragCancelled = false;
                _preDragPosition = Position;
                _dragging = true;
            }
            else
            {
                _dragging = false;
                if (_dragCancelled)
                    _dragCancelled = false;
                else if (WindowName != null)
                    GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, Size, UiScaleApplier.Instance != null ? UiScaleApplier.Instance.Factor : 1f, null, (Vector2I)GetTree().Root.GetVisibleRect().Size);
            }
        }
        else if (@event is InputEventMouseMotion motion && _dragging)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _dragging = false;
                if (_dragCancelled)
                    _dragCancelled = false;
                else if (WindowName != null)
                    GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, Size, UiScaleApplier.Instance != null ? UiScaleApplier.Instance.Factor : 1f, null, (Vector2I)GetTree().Root.GetVisibleRect().Size);
                return;
            }
            Position += motion.Relative;
        }
    }

    public void CancelDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _dragCancelled = true;
        Position = _preDragPosition;
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
            GameManager.Instance.CharacterSettings.SetWindowVisible(WindowName, Visible);
    }

    protected virtual void OnClosePressed()
    {
        Hide();
        if (WindowName != null)
            GameManager.Instance.CharacterSettings.SetWindowVisible(WindowName, false);
    }
}
