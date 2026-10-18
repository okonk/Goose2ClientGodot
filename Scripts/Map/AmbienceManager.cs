using System;
using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.Map;

public partial class AmbienceManager : Node2D
{
    private ulong _cycleStartMsec;
    private float _startAlpha;
    private float _endAlpha;
    private uint _tintColor;
    private long _durationMs;
    private bool _listening;

    public float Darkness { get; private set; }
    public float TimeOfDay { get; private set; }
    public WeatherType CurrentWeather { get; private set; } = WeatherType.Clear;
    public bool IsNight => AmbienceMath.IsNight(TimeOfDay);
    public event Action? WeatherChanged;
    internal CanvasModulate TintNode { get; private set; } = null!;

    public override void _Ready()
    {
        TintNode = new CanvasModulate { Name = "Tint", Color = Colors.White };
        AddChild(TintNode);
        GameManager.Instance.PacketManager.Listen<MapAmbiencePacket>(OnAmbience);
        _listening = true;
    }

    public override void _ExitTree()
    {
        if (!_listening) return;
        _listening = false;
        GameManager.Instance?.PacketManager?.Remove<MapAmbiencePacket>(OnAmbience);
    }

    public override void _Process(double delta)
    {
        var elapsed = (long)(Time.GetTicksMsec() - _cycleStartMsec);
        TimeOfDay = AmbienceMath.TimeOfDay(elapsed, _durationMs);
        Darkness = AmbienceMath.Darkness(_startAlpha, _endAlpha, elapsed, _durationMs);
        TintNode.Color = AmbienceMath.ModulateColor(_tintColor, Darkness);
    }

    public void Setup(Camera2D camera)
    {
    }

    private void OnAmbience(object packetObj)
    {
        var p = (MapAmbiencePacket)packetObj;
        _startAlpha = p.StartAlpha;
        _endAlpha = p.EndAlpha;
        _tintColor = p.TintColor;
        _durationMs = p.DurationMs;
        _cycleStartMsec = Time.GetTicksMsec() - (ulong)p.ElapsedMs;

        if (p.Weather != CurrentWeather)
        {
            CurrentWeather = p.Weather;
            TransitionToWeather(CurrentWeather);
            WeatherChanged?.Invoke();
        }
    }

    private void TransitionToWeather(WeatherType weather)
    {
    }
}
