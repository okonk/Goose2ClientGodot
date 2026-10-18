using Godot;

namespace Goose2Client.Map;

public partial class LocalPlayerLight : PointLight2D
{
    private readonly AmbienceManager _ambience;

    public LocalPlayerLight(AmbienceManager ambience)
    {
        _ambience = ambience;
    }

    public override void _Ready()
    {
        Texture = GD.Load<Texture2D>("res://Assets/Textures/light_gradient.png");
        BlendMode = Light2D.BlendModeEnum.Add;
        TextureScale = 80f / 128f;
        Color = new Color(0xFFF0E0FF);
        Position = new Vector2(0, -24);
        Energy = 0;
    }

    public override void _Process(double delta)
    {
        Energy = AmbienceMath.LocalLightIntensity * _ambience.Darkness;
    }
}
