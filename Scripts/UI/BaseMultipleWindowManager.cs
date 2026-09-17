using System.Collections.Generic;
using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Abstract manager for multi-instance windows. Routes packets directly to the
/// correct window instance by WindowId (no packet buffer — packets arrive on main thread).
/// This generic base is NEVER attached to a scene; only concrete subclasses are used.
/// </summary>
public abstract partial class BaseMultipleWindowManager<T> : Node where T : BaseMultipleWindow
{
    private readonly Dictionary<int, T> _windows = new();
    private bool _listenersRegistered;

    public abstract string PrefabPath { get; }
    public abstract bool MatchesFrame(WindowFrames frame);

    public override void _Ready()
    {
        GameManager.Instance.PacketManager.Listen<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Listen<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Listen<WindowLinePacket>(OnWindowLine);
        _listenersRegistered = true;
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<MakeWindowPacket>(OnMakeWindow);
        GameManager.Instance.PacketManager.Remove<EndWindowPacket>(OnEndWindow);
        GameManager.Instance.PacketManager.Remove<WindowLinePacket>(OnWindowLine);
    }

    private void OnMakeWindow(object o)
    {
        var p = (MakeWindowPacket)o;
        if (!MatchesFrame(p.WindowFrame)) return;

        if (!_windows.TryGetValue(p.WindowId, out var w))
        {
            var scene = GD.Load<PackedScene>(PrefabPath);
            w = scene.Instantiate<T>();
            AddChild(w);
            w.OnCloseWindow = OnCloseWindow;
            CascadePosition(w);

            _windows[p.WindowId] = w;
        }

        w.OnMakeWindow(p);
    }

    // Same-frame dialogs all default to the same position (centered or the shared saved
    // position), so step each new window down-right from the ones already open to keep it
    // visible.
    private void CascadePosition(T w)
    {
        if (_windows.Count == 0) return;
        var step = 30f * (UiScaleApplier.Instance?.Factor ?? 1f);
        w.Position += new Vector2(step, step) * _windows.Count;
    }

    public void OnCloseWindow(BaseMultipleWindow window)
    {
        _windows.Remove(window.WindowId);
    }

    private void OnEndWindow(object o)
    {
        var p = (EndWindowPacket)o;
        if (_windows.TryGetValue(p.WindowId, out var w))
            w.OnEndWindow();
    }

    private void OnWindowLine(object o)
    {
        var p = (WindowLinePacket)o;
        if (_windows.TryGetValue(p.WindowId, out var w))
            w.OnWindowLine(p);
    }
}
