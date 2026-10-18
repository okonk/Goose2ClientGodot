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

    private GpuParticles2D? _rain, _snow, _storm;
    private ColorRect? _fog, _flash;
    private Tween? _weatherTween, _flashTween;
    private float _lightningTimer;
    private const float LightningMinInterval = 5f;
    private const float LightningMaxInterval = 15f;
    private static Texture2D? _particleTexture;

    public override void _Ready()
    {
        TintNode = new CanvasModulate { Name = "Tint", Color = Colors.White };
        AddChild(TintNode);
        GameManager.Instance.PacketManager.Listen<MapAmbiencePacket>(OnAmbience);
        _listening = true;
    }

    public override void _ExitTree()
    {
        _weatherTween?.Kill();
        _flashTween?.Kill();
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

        if (_flash == null || CurrentWeather != WeatherType.Storm) return;
        _lightningTimer -= (float)delta;
        if (_lightningTimer <= 0)
        {
            FlashLightning();
            _lightningTimer = (float)GD.RandRange(LightningMinInterval, LightningMaxInterval);
        }
    }

    public void Setup(Camera2D camera)
    {
        var size = GetViewport().GetVisibleRect().Size;
        _rain = CreateWeatherParticles(camera, "Rain", 800f, new Color(0.63f, 0.77f, 1f, 0.7f), size);
        _snow = CreateWeatherParticles(camera, "Snow", 150f, new Color(1f, 1f, 1f, 0.8f), size);
        _storm = CreateWeatherParticles(camera, "Storm", 1200f, new Color(0.5f, 0.6f, 0.8f, 0.8f), size);

        var layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);
        _fog = new ColorRect { Color = new Color(0.5f, 0.5f, 0.5f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _fog.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_fog);
        _flash = new ColorRect { Color = new Color(1f, 1f, 1f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_flash);

        _lightningTimer = (float)GD.RandRange(LightningMinInterval, LightningMaxInterval);
    }

    private GpuParticles2D CreateWeatherParticles(Camera2D parent, string name, float speed, Color color, Vector2 size)
    {
        // Extents/lifetime are fixed at Setup; a viewport resize is picked up on the next map change.
        var lifetime = (size.Y + 128) / (speed * 0.8f);
        var material = new ParticleProcessMaterial
        {
            Direction = new Vector3(0.1f, 1f, 0f),
            Spread = 10f,
            Gravity = Vector3.Zero,
            InitialVelocityMin = speed * 0.8f,
            InitialVelocityMax = speed,
            Color = color,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(size.X * 0.6f, 1f, 1f),
        };
        var p = new GpuParticles2D
        {
            Name = name,
            ZIndex = 100,
            Emitting = false,
            Amount = 800,
            Lifetime = lifetime,
            Preprocess = lifetime,
            Texture = _particleTexture ??= CreateParticleTexture(),
            ProcessMaterial = material,
            Position = new Vector2(0, -size.Y / 2f - 64),
        };
        parent.AddChild(p);
        return p;
    }

    private static Texture2D CreateParticleTexture()
    {
        const int size = 16;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var center = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                image.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp(1f - dist / center, 0f, 1f)));
            }
        return ImageTexture.CreateFromImage(image);
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
        if (_fog == null) return;
        _weatherTween?.Kill();
        _weatherTween = CreateTween();
        _weatherTween.TweenProperty(_fog, "color:a", weather == WeatherType.Fog ? 0.3f : 0f, 2.0f);

        var rain = _rain!;
        var snow = _snow!;
        var storm = _storm!;
        rain.Emitting = weather == WeatherType.Rain;
        snow.Emitting = weather == WeatherType.Snow;
        storm.Emitting = weather == WeatherType.Storm;

        if (weather != WeatherType.Storm)
            _lightningTimer = (float)GD.RandRange(LightningMinInterval, LightningMaxInterval);
    }

    private void FlashLightning()
    {
        _flashTween?.Kill();
        _flashTween = CreateTween();
        _flashTween.TweenProperty(_flash, "color:a", 0.8f, 0.05f);
        _flashTween.TweenProperty(_flash, "color:a", 0f, 0.1f);
    }
}
