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
    public abstract WindowFrames WindowFrame { get; }

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
        if (p.WindowFrame != WindowFrame) return;

        if (!_windows.TryGetValue(p.WindowId, out var w))
        {
            var scene = GD.Load<PackedScene>(PrefabPath);
            w = scene.Instantiate<T>();
            AddChild(w);
            w.OnCloseWindow = OnCloseWindow;
            _windows[p.WindowId] = w;
        }

        w.OnMakeWindow(p);
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
