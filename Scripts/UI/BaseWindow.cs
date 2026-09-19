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
    protected Control Background { get; private set; }

    private static readonly Texture2D CloseIcon = GD.Load<Texture2D>("res://Assets/UI/window-close.svg");
    private static BaseWindow _activeWindow;

    private Panel _panelBg;
    private Panel _titleBarBg;
    private bool _chromeReady;

    /// <summary>Windows whose Background art is a shaped sprite rather than a rectangular
    /// frame (the hotbar) opt out and keep their texture.</summary>
    protected virtual bool ThemedChrome => true;

    public string Title { set { if (TitleLabel != null) TitleLabel.Text = value; } }

    /// <summary>First-run visibility (tscn/design state before any saved settings apply).
    /// Server-spawned and toggle-closed windows override to false.</summary>
    protected virtual bool DefaultVisible => true;

    /// <summary>Restore this window's first-run visibility and default position
    /// (call after <c>CharacterSettings.ResetWindowSettings</c> so no saved entry remains).</summary>
    public virtual void ResetToDefault()
    {
        Visible = DefaultVisible;
        RepositionFromSaved();
    }

    public override void _Ready()
    {
        // tscn size is the 1x base for placement math; relayout (below, via ScaleRegister)
        // resizes the frame after this capture.
        _tscnSize = Size;

        _titleBar = GetNodeOrNull<Control>("TitleBar");
        _closeButton = GetNodeOrNull<Button>("TitleBar/CloseButton");
        TitleLabel = GetNodeOrNull<Label>("TitleBar/TitleLabel");
        Content = GetNodeOrNull<Control>("Content");
        Background = GetNodeOrNull<Control>("Background");

        BuildChrome();

        // The full-rect Content (MouseFilter=Pass) is drawn on top of the TitleBar and
        // swallows its clicks — Pass forwards unhandled events to the PARENT, never to the
        // TitleBar sibling — which kills title-bar dragging. Make Content transparent to the
        // mouse so the TitleBar receives drag clicks. Interactive descendants (slots, buttons,
        // bars) keep their own MouseFilter and are unaffected (mouse_filter does not cascade).
        if (Content != null)
            Content.MouseFilter = MouseFilterEnum.Ignore;

        if (WindowName != null)
        {
            var ws = GameManager.Instance?.CharacterSettings?.GetWindowSettings(WindowName);
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

        _chromeReady = true;

        // Deferred so subclass _Ready build code runs first; their synchronous ScaleRegister
        // calls make this a no-op (idempotent via _scaleRegistered).
        Callable.From(() => ScaleRegister()).CallDeferred();
    }

    /// <summary>Swaps the baked window art for themed panels and normalises the title bar,
    /// so every window shares one frame, one title style and one close button.</summary>
    private void BuildChrome()
    {
        if (!ThemedChrome) return;

        if (Background is TextureRect art)
        {
            int index = art.GetIndex();
            RemoveChild(art);
            art.QueueFree();

            _panelBg = new Panel
            {
                Name = "Background",
                ThemeTypeVariation = "WindowPanel",
                MouseFilter = MouseFilterEnum.Ignore
            };
            _panelBg.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_panelBg);
            MoveChild(_panelBg, index);
            Background = _panelBg;
        }

        // A zero-height TitleBar is a bare drag handle (hotbar), not a caption.
        if (_titleBar == null || _titleBar.OffsetBottom - _titleBar.OffsetTop <= 0)
            return;

        _titleBarBg = new Panel
        {
            Name = "TitleBarBackground",
            ThemeTypeVariation = "WindowTitleBar",
            MouseFilter = MouseFilterEnum.Ignore
        };
        _titleBarBg.SetAnchorsPreset(LayoutPreset.FullRect);
        _titleBar.AddChild(_titleBarBg);
        _titleBar.MoveChild(_titleBarBg, 0);

        if (TitleLabel == null)
        {
            TitleLabel = new Label
            {
                Name = "TitleLabel",
                Text = WindowName ?? "",
                MouseFilter = MouseFilterEnum.Pass,
                VerticalAlignment = VerticalAlignment.Center,
                // Without clipping the label's minimum size is the full text width; in a
                // narrow window (CombineBag, 69px) that pushes the label over the close
                // button and its Pass filter forwards X-clicks to the title-bar drag handle.
                ClipText = true
            };
            TitleLabel.SetAnchorsPreset(LayoutPreset.FullRect);
            TitleLabel.OffsetLeft = 6f;
            TitleLabel.OffsetRight = -20f;
            _titleBar.AddChild(TitleLabel);
        }
        TitleLabel.ThemeTypeVariation = "WindowTitle";

        if (_closeButton != null)
        {
            _closeButton.ThemeTypeVariation = "WindowCloseButton";
            _closeButton.Flat = false;
            _closeButton.Text = "";
            _closeButton.Icon = CloseIcon;
            _closeButton.ExpandIcon = true;
            _closeButton.FocusMode = FocusModeEnum.None;
            _closeButton.TooltipText = "Close";
        }
    }

    /// <summary>Raises this window above its siblings and gives it the focused frame.</summary>
    public void Activate()
    {
        if (_activeWindow == this)
        {
            MoveToFront();
            return;
        }

        if (GodotObject.IsInstanceValid(_activeWindow))
            _activeWindow.SetChromeActive(false);

        _activeWindow = this;
        SetChromeActive(true);
        MoveToFront();
    }

    private void SetChromeActive(bool active)
    {
        if (_panelBg != null)
            _panelBg.ThemeTypeVariation = active ? "WindowPanelActive" : "WindowPanel";
        if (_titleBarBg != null)
            _titleBarBg.ThemeTypeVariation = active ? "WindowTitleBarActive" : "WindowTitleBar";
        if (TitleLabel != null && ThemedChrome)
            TitleLabel.ThemeTypeVariation = active ? "WindowTitleActive" : "WindowTitle";
    }

    private void Deactivate()
    {
        if (_activeWindow != this) return;
        _activeWindow = null;
        SetChromeActive(false);
    }

    // Windows are also shown by server packets and by subclasses that bypass Toggle, so
    // focus follows visibility rather than any one call site.
    public override void _Notification(int what)
    {
        if (what != NotificationVisibilityChanged || !_chromeReady)
            return;

        if (Visible)
            Activate();
        else
            Deactivate();
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible || @event is not InputEventMouseButton { Pressed: true } mb)
            return;
        if (mb.ButtonIndex != MouseButton.Left && mb.ButtonIndex != MouseButton.Right)
            return;
        if (!GetGlobalRect().HasPoint(mb.Position) || !IsTopmostAt(mb.Position))
            return;

        Activate();
    }

    // Siblings after this one in tree order draw on top, so a hit on any of them
    // is not a hit on this window.
    private bool IsTopmostAt(Vector2 point)
    {
        var parent = GetParent();
        if (parent == null) return true;

        bool above = false;
        foreach (Node sibling in parent.GetChildren())
        {
            if (sibling == this) { above = true; continue; }
            if (!above) continue;
            if (sibling is Control c && c.Visible && c.GetGlobalRect().HasPoint(point))
                return false;
        }
        return true;
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
        if (!placed && !legacy && WindowName == "Hotbar")
        {
            var ap = UiScaleApplier.Instance;
            Position = WindowPlacement.HotbarDefault(canvas, Size,
                ap != null ? ap.Factor : 1f, DefaultWindowLayout.For(WindowName), _tscnSize);
            return;
        }
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
                    // Live Visible, not null: a fresh WindowSettings entry defaults to Visible=false,
                    // and a null here would persist that and hide the window on next login.
                    GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, Size, UiScaleApplier.Instance != null ? UiScaleApplier.Instance.Factor : 1f, Visible, (Vector2I)GetTree().Root.GetVisibleRect().Size);
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
                    GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, Size, UiScaleApplier.Instance != null ? UiScaleApplier.Instance.Factor : 1f, Visible, (Vector2I)GetTree().Root.GetVisibleRect().Size);
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
