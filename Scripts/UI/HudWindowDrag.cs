using Godot;
using System.Collections.Generic;

namespace Goose2Client.UI;

/// <summary>
/// Makes a plain HUD Control (no title bar) draggable from any point inside it and
/// persists its position in CharacterSettings. Vitals/Party/Buff windows are
/// display-only Controls, not BaseWindow, so they get whole-surface drag instead of
/// title-bar drag.
/// </summary>
public static class HudWindowDrag
{
    // A click (e.g. double-clicking a buff to kill it) must not move the window.
    private const float DragThreshold = 4f;

    public static void Attach(Control window, string settingsKey)
    {
        var drag = new Drag(window, settingsKey);
        window.GuiInput += drag.Handle;
        // Descendants with MouseFilter.Stop (bars, buff panels, member frames) consume
        // presses before the window sees them, so every descendant Control needs the
        // handler too. Once a control receives the press, Godot routes the follow-up
        // motion to the same control, so one press is tracked end-to-end.
        foreach (var child in EnumerateControls(window))
            child.GuiInput += drag.Handle;
    }

    // Restore the saved position, or leave the design offsets the scale layout just set when
    // none. Called from each window's Relayout, after UiScaleLayout.Apply reset the root to
    // its design size/position at the current factor.
    public static void RepositionFromSaved(Control window, string settingsKey)
    {
        if (!window.IsInsideTree()) return;
        var ws = GameManager.Instance?.CharacterSettings?.GetWindowSettings(settingsKey);
        if (ws == null || !ws.Placed) return;

        var canvas = (Vector2I)window.GetTree().Root.GetVisibleRect().Size;
        var savedCanvas = ws.CanvasSize != default ? ws.CanvasSize : WindowPlacement.LegacyCanvas;
        var savedSize = ws.Size != default ? ws.Size : window.Size;
        var savedFactor = ws.Factor > 0f ? ws.Factor : 1f;
        var factor = UiScaleApplier.Instance?.Factor ?? 1f;
        // No title bar: contain the whole window (allowance = full height), matching the drag clamp.
        window.Position = WindowPlacement.ResolveScaled(ws.Position, savedSize, savedFactor, savedCanvas,
            window.Size, factor, canvas, (int)window.Size.Y);
    }

    private static IEnumerable<Control> EnumerateControls(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Control c) yield return c;
            foreach (var nested in EnumerateControls(child))
                yield return nested;
        }
    }

    private sealed class Drag
    {
        private readonly Control _window;
        private readonly string _settingsKey;
        private Vector2 _startPos;
        private Vector2 _startMouse;
        private bool _tracking;
        private bool _dragging;

        public Drag(Control window, string settingsKey)
        {
            _window = window;
            _settingsKey = settingsKey;
        }

        public void Handle(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
            {
                if (mb.Pressed)
                {
                    _tracking = true;
                    _dragging = false;
                    _startPos = _window.Position;
                    _startMouse = _window.GetGlobalMousePosition();
                }
                else
                {
                    bool moved = _dragging;
                    _tracking = false;
                    _dragging = false;
                    if (moved) Save();
                }
            }
            else if (e is InputEventMouseMotion && _tracking)
            {
                if (!Input.IsMouseButtonPressed(MouseButton.Left))
                {
                    bool moved = _dragging;
                    _tracking = false;
                    _dragging = false;
                    if (moved) Save();
                    return;
                }
                var delta = _window.GetGlobalMousePosition() - _startMouse;
                if (!_dragging && delta.Length() > DragThreshold)
                    _dragging = true;
                if (_dragging)
                    _window.Position = Clamp(_startPos + delta);
            }
        }

        private void Save()
        {
            var cs = GameManager.Instance?.CharacterSettings;
            if (cs == null) return;
            var canvas = (Vector2I)_window.GetTree().Root.GetVisibleRect().Size;
            // visible=null: these windows' visibility is game-driven, not user-toggled.
            cs.SetWindowSetting(_settingsKey, _window.Position, _window.Size,
                UiScaleApplier.Instance?.Factor ?? 1f, null, canvas);
        }

        private Vector2 Clamp(Vector2 pos)
        {
            var canvas = _window.GetTree().Root.GetVisibleRect().Size;
            float x = Mathf.Clamp(pos.X, 0f, Mathf.Max(0f, canvas.X - _window.Size.X));
            float y = Mathf.Clamp(pos.Y, 0f, Mathf.Max(0f, canvas.Y - _window.Size.Y));
            return new Vector2(x, y);
        }
    }
}
