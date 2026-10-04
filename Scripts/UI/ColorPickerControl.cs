using System;
using Godot;

namespace Goose2Client.UI;

/// <summary>
/// HSL color picker: saturation/lightness swatch at a fixed hue, hue and lightness bars,
/// and the RGB(A) channels they stay in sync with. Hosted by windows that need a color choice.
/// </summary>
public partial class ColorPickerControl : Control
{
    private TextureRect _swatch = null!;
    private TextureRect _swatchCursor = null!;
    private TextureRect _hueBar = null!;
    private TextureRect _hueCursor = null!;
    private TextureRect _lightBar = null!;
    private TextureRect _lightCursor = null!;
    private Label _aLabel = null!;
    private HSlider _rSlider = null!;
    private HSlider _gSlider = null!;
    private HSlider _bSlider = null!;
    private HSlider _aSlider = null!;
    private Label _rValue = null!;
    private Label _gValue = null!;
    private Label _bValue = null!;
    private Label _aValue = null!;

    private bool _ready;
    private bool _showAlpha = true;
    private bool _suppress;
    private int _r = 255;
    private int _g = 255;
    private int _b = 255;
    private int _a = 255;
    private float _hue;
    private float _lastHue = -1f;
    private bool _preservingHue;

    public event Action<Color>? ColorChanged;

    public Color Color => new(_r / 255f, _g / 255f, _b / 255f, _showAlpha ? _a / 255f : 1f);

    [Export]
    public bool ShowAlpha
    {
        get => _showAlpha;
        set
        {
            _showAlpha = value;
            ApplyAlphaVisibility();
        }
    }

    public override void _Ready()
    {
        _swatch = GetNode<TextureRect>("Swatch");
        _swatchCursor = GetNode<TextureRect>("Swatch/Cursor");
        _hueBar = GetNode<TextureRect>("HueBar");
        _hueCursor = GetNode<TextureRect>("HueBar/Cursor");
        _lightBar = GetNode<TextureRect>("LightBar");
        _lightCursor = GetNode<TextureRect>("LightBar/Cursor");
        _swatch.GuiInput += OnSwatchGuiInput;
        _hueBar.GuiInput += OnHueGuiInput;
        _lightBar.GuiInput += OnLightBarGuiInput;
        _swatch.TextureFilter = CanvasItem.TextureFilterEnum.Linear;

        _aLabel = GetNode<Label>("ALabel");
        _rSlider = GetNode<HSlider>("RSlider");
        _gSlider = GetNode<HSlider>("GSlider");
        _bSlider = GetNode<HSlider>("BSlider");
        _aSlider = GetNode<HSlider>("ASlider");
        _rValue = GetNode<Label>("RValue");
        _gValue = GetNode<Label>("GValue");
        _bValue = GetNode<Label>("BValue");
        _aValue = GetNode<Label>("AValue");
        _rSlider.ValueChanged += v => { _r = (int)v; _rValue.Text = _r.ToString(); SyncHsl(); Emit(); };
        _gSlider.ValueChanged += v => { _g = (int)v; _gValue.Text = _g.ToString(); SyncHsl(); Emit(); };
        _bSlider.ValueChanged += v => { _b = (int)v; _bValue.Text = _b.ToString(); SyncHsl(); Emit(); };
        _aSlider.ValueChanged += v => { _a = (int)v; _aValue.Text = _a.ToString(); Emit(); };

        _ready = true;
        ApplyAlphaVisibility();
        SyncHsl();
    }

    /// <summary>Set the picker without notifying the host, and force the hue textures to rebuild.</summary>
    public void SetColor(Color color)
    {
        _r = Mathf.RoundToInt(color.R * 255f);
        _g = Mathf.RoundToInt(color.G * 255f);
        _b = Mathf.RoundToInt(color.B * 255f);
        _a = Mathf.RoundToInt(color.A * 255f);
        _hue = 0f;
        _lastHue = -1f;
        _preservingHue = false;
        if (!_ready)
            return;
        _suppress = true;
        _rSlider.Value = _r;
        _gSlider.Value = _g;
        _bSlider.Value = _b;
        _aSlider.Value = _a;
        _rValue.Text = _r.ToString();
        _gValue.Text = _g.ToString();
        _bValue.Text = _b.ToString();
        _aValue.Text = _a.ToString();
        _suppress = false;
        SyncHsl();
    }

    private void ApplyAlphaVisibility()
    {
        if (!_ready)
            return;
        _aLabel.Visible = _showAlpha;
        _aSlider.Visible = _showAlpha;
        _aValue.Visible = _showAlpha;
    }

    private void Emit()
    {
        if (!_suppress)
            ColorChanged?.Invoke(Color);
    }

    private void BuildSwatchTexture(float hue)
    {
        const int size = 64;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var (r, g, b) = HslColor.ToRgb(hue, x / (size - 1f), 1f - y / (size - 1f));
                img.SetPixel(x, y, new Color(r / 255f, g / 255f, b / 255f, 1f));
            }
        }
        _swatch.Texture = ImageTexture.CreateFromImage(img);
    }

    private void BuildLightBarTexture(float hue)
    {
        var (r, g, b) = HslColor.ToRgb(hue, 1f, 0.5f);
        var grad = new Gradient
        {
            Colors = new[] { Colors.Black, new Color(r / 255f, g / 255f, b / 255f), Colors.White },
            Offsets = new[] { 0f, 0.5f, 1f },
        };
        _lightBar.Texture = new GradientTexture2D
        {
            Gradient = grad,
            Width = 128,
            Height = 12,
            FillFrom = new Vector2(0, 0.5f),
            FillTo = new Vector2(1, 0.5f),
        };
    }

    public void SyncHsl()
    {
        if (!_ready)
            return;
        var (h, s, l) = HslColor.FromRgb(_r, _g, _b);
        // The swatch and lightness bar work at a fixed hue; re-deriving it from the
        // quantized RGB round-trip would drift it (large near grey/extreme lightness).
        if (!_preservingHue && s > 0f) _hue = h;
        if (Math.Abs(_hue - _lastHue) > 0.5f)
        {
            _lastHue = _hue;
            BuildSwatchTexture(_hue);
            BuildLightBarTexture(_hue);
        }
        _swatchCursor.Position = LockCursor(new Vector2(s * _swatch.Size.X, (1f - l) * _swatch.Size.Y), _swatchCursor.Size, _swatch.Size);
        _hueCursor.Position = LockCursor(new Vector2(_hue / 360f * _hueBar.Size.X, _hueBar.Size.Y / 2f), _hueCursor.Size, _hueBar.Size);
        _lightCursor.Position = LockCursor(new Vector2(l * _lightBar.Size.X, _lightBar.Size.Y / 2f), _lightCursor.Size, _lightBar.Size);
    }

    private static Vector2 LockCursor(Vector2 center, Vector2 cursorSize, Vector2 parentSize)
    {
        var pos = center - cursorSize / 2f;
        pos.X = Mathf.Clamp(pos.X, 0f, Mathf.Max(0f, parentSize.X - cursorSize.X));
        pos.Y = Mathf.Clamp(pos.Y, 0f, Mathf.Max(0f, parentSize.Y - cursorSize.Y));
        return pos;
    }

    private void OnSwatchGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
            ApplySwatchAt(mb.Position);
        else if (@event is InputEventMouseMotion mm && Input.IsMouseButtonPressed(MouseButton.Left))
            ApplySwatchAt(mm.Position);
    }

    private void ApplySwatchAt(Vector2 pos)
    {
        var (_, s, l) = HslColor.FromRgb(_r, _g, _b);
        var ns = Mathf.Clamp(pos.X / _swatch.Size.X, 0f, 1f);
        var nl = 1f - Mathf.Clamp(pos.Y / _swatch.Size.Y, 0f, 1f);
        _preservingHue = true;
        SetRgb(HslColor.ToRgb(_hue, ns, nl));
        _preservingHue = false;
        _swatchCursor.Position = LockCursor(pos, _swatchCursor.Size, _swatch.Size);
    }

    private void OnHueGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
            ApplyHueAt(mb.Position);
        else if (@event is InputEventMouseMotion mm && Input.IsMouseButtonPressed(MouseButton.Left))
            ApplyHueAt(mm.Position);
    }

    private void ApplyHueAt(Vector2 pos)
    {
        var (_, s, l) = HslColor.FromRgb(_r, _g, _b);
        _hue = Mathf.Clamp(pos.X / _hueBar.Size.X, 0f, 1f) * 360f;
        SetRgb(HslColor.ToRgb(_hue, s, l));
        _hueCursor.Position = LockCursor(pos, _hueCursor.Size, _hueBar.Size);
    }

    private void OnLightBarGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
            ApplyLightAt(mb.Position);
        else if (@event is InputEventMouseMotion mm && Input.IsMouseButtonPressed(MouseButton.Left))
            ApplyLightAt(mm.Position);
    }

    private void ApplyLightAt(Vector2 pos)
    {
        var (_, s, l) = HslColor.FromRgb(_r, _g, _b);
        var nl = Mathf.Clamp(pos.X / _lightBar.Size.X, 0f, 1f);
        _preservingHue = true;
        SetRgb(HslColor.ToRgb(_hue, s, nl));
        _preservingHue = false;
        _lightCursor.Position = LockCursor(pos, _lightCursor.Size, _lightBar.Size);
    }

    private void SetRgb((int R, int G, int B) rgb)
    {
        _r = rgb.R;
        _g = rgb.G;
        _b = rgb.B;
        _suppress = true;
        _rSlider.Value = _r;
        _gSlider.Value = _g;
        _bSlider.Value = _b;
        _rValue.Text = _r.ToString();
        _gValue.Text = _g.ToString();
        _bValue.Text = _b.ToString();
        _suppress = false;
        // Suppressed sliders never fire ValueChanged, so the bars sync from here instead.
        SyncHsl();
        Emit();
    }
}
