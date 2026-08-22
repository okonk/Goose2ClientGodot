using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Goose2Client.UI;

namespace Goose2Client;

public enum ApplyReason
{
    Startup,
    UserCommit,
    AutoResize
}

public class UiScaleApplier
{
    public static UiScaleApplier Instance { get; internal set; }

    public UiScale Scale { get; } = new();

    private Theme _theme;

    public Theme Theme => _theme ??= GD.Load<Theme>("res://Assets/UI/GameTheme.tres");

    public sealed class WindowRegistration
    {
        public WindowRegistration(IScalableWindow window, Control controlRef)
        {
            Window = window;
            ControlRef = controlRef;
        }

        public IScalableWindow Window { get; }
        public Control ControlRef { get; }
    }

    private readonly List<WindowRegistration> _windows = new();
    private readonly List<(Control C, StringName Prop, float Base)> _fonts = new();
    private bool _appliedOnce;

    public float Factor => Scale.CurrentFactor;

    public int ScaleSize(float basePx) => Scale.ScaleSize(basePx);

    public IReadOnlyList<WindowRegistration> RegisteredWindows => _windows;

    public WindowRegistration RegisterWindow(IScalableWindow w)
    {
        if (w == null)
            throw new ArgumentNullException(nameof(w));
        if (w is not Control c)
            throw new ArgumentException("window must be a Control", nameof(w));

        var existing = _windows.Find(r => r.Window == w);
        if (existing != null)
            return existing;

        var reg = new WindowRegistration(w, c);
        _windows.Add(reg);
        return reg;
    }

    // Ownership by ancestry at unregister time, not by registration: ApplyFontSize runs
    // during the window's _Ready, before its own registration.
    public void UnregisterWindow(IScalableWindow w)
    {
        if (w == null)
            return;
        if (w is not Control root)
            return;

        _windows.RemoveAll(r => r.Window == w);
        _fonts.RemoveAll(e => !GodotObject.IsInstanceValid(e.C) || e.C == root || root.IsAncestorOf(e.C));
    }

    public void ApplyFontSize(Control c, float basePx)
        => ApplyFontSize(c, basePx, new StringName("font_size"));

    public void ApplyFontSize(Control c, float basePx, StringName prop)
    {
        c.AddThemeFontSizeOverride(prop, ScaleSize(basePx));
        _fonts.Add((c, prop, basePx));
    }

    public bool TryGetFontBase(Control c, StringName prop, out float basePx)
    {
        int i = _fonts.FindIndex(e => e.C == c && e.Prop == prop);
        basePx = i >= 0 ? _fonts[i].Base : 0f;
        return i >= 0;
    }

    public void Apply(float factor, ApplyReason reason)
    {
        var f = UiScale.NormalizeFactor(factor);
        if (f == Scale.CurrentFactor && _appliedOnce)
            return;
        _appliedOnce = true;
        Scale.CurrentFactor = f;

        // Part 2 seam: the scale-commit BaseWindow CancelDrag() pass inserts here, before the tooltip hide.
        if (TooltipManager.Instance != null)
            TooltipManager.Instance.HideAll();

        Theme.SetDefaultFontSize(ScaleSize(10));

        // Removal during the apply foreach would throw; collect dead refs first.
        var invalid = _fonts
            .Where(e => !GodotObject.IsInstanceValid(e.C))
            .Select(e => e.C)
            .Distinct()
            .ToList();
        _fonts.RemoveAll(e => invalid.Contains(e.C));
        _windows.RemoveAll(r => !GodotObject.IsInstanceValid(r.ControlRef));
        foreach (var (c, prop, px) in _fonts)
        {
            if (c.IsInsideTree())
                c.AddThemeFontSizeOverride(prop, ScaleSize(px));
        }

        foreach (var r in _windows)
            r.Window.Relayout();

        foreach (var r in _windows)
        {
            if (r.Window is BaseWindow bw)
                bw.RepositionFromSaved();
        }
    }
}
