using System;
using Godot;
using Goose2Client;
using Goose2Client.Character;

namespace Goose2Client.UI;

public partial class HairdyeWindow : BaseWindow
{
    protected override bool DefaultVisible => false;

    private CustomPreviewControl _preview;
    private TextureRect _swatch;
    private TextureRect _swatchCursor;
    private TextureRect _hueBar;
    private TextureRect _hueCursor;
    private TextureRect _lightBar;
    private TextureRect _lightCursor;
    private HSlider _rSlider;
    private HSlider _gSlider;
    private HSlider _bSlider;
    private HSlider _aSlider;
    private Label _rValue;
    private Label _gValue;
    private Label _bValue;
    private Label _aValue;
    private Button _dyeButton;

    private int _r = CustomWindowMetrics.DefaultR;
    private int _g = CustomWindowMetrics.DefaultG;
    private int _b = CustomWindowMetrics.DefaultB;
    private int _a = CustomWindowMetrics.DefaultA;
    private float _lastHue = -1f;
    private float _hue;
    private bool _preservingHue;

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _preview = GetNode<CustomPreviewControl>("Content/Preview");
        _preview.HideSlot(CharacterSlot.Helm);

        _swatch = GetNode<TextureRect>("Content/Swatch");
        _swatchCursor = GetNode<TextureRect>("Content/Swatch/Cursor");
        _hueBar = GetNode<TextureRect>("Content/HueBar");
        _hueCursor = GetNode<TextureRect>("Content/HueBar/Cursor");
        _lightBar = GetNode<TextureRect>("Content/LightBar");
        _lightCursor = GetNode<TextureRect>("Content/LightBar/Cursor");
        _swatch.GuiInput += OnSwatchGuiInput;
        _hueBar.GuiInput += OnHueGuiInput;
        _lightBar.GuiInput += OnLightBarGuiInput;
        _swatch.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
        SyncHsl();

        _rSlider = GetNode<HSlider>("Content/RSlider");
        _gSlider = GetNode<HSlider>("Content/GSlider");
        _bSlider = GetNode<HSlider>("Content/BSlider");
        _aSlider = GetNode<HSlider>("Content/ASlider");
        _rValue = GetNode<Label>("Content/RValue");
        _gValue = GetNode<Label>("Content/GValue");
        _bValue = GetNode<Label>("Content/BValue");
        _aValue = GetNode<Label>("Content/AValue");
        _rSlider.ValueChanged += v => { _r = (int)v; _rValue.Text = _r.ToString(); UpdateTint(); };
        _gSlider.ValueChanged += v => { _g = (int)v; _gValue.Text = _g.ToString(); UpdateTint(); };
        _bSlider.ValueChanged += v => { _b = (int)v; _bValue.Text = _b.ToString(); UpdateTint(); };
        _aSlider.ValueChanged += v => { _a = (int)v; _aValue.Text = _a.ToString(); UpdateTint(); };

        _dyeButton = GetNode<Button>("Content/DyeButton");
        _dyeButton.Pressed += DyePressed;

        ScaleRegister();
    }

    public override void Relayout()
    {
        base.Relayout();
        _preview.Refresh();
        SyncHsl();
    }

    public void Open()
    {
        Visible = true;
        UpdateTint();
        _preview.Refresh();
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

    private void SetRgb(int r, int g, int b)
    {
        _rSlider.Value = r;
        _gSlider.Value = g;
        _bSlider.Value = b;
    }

    private void SyncHsl()
    {
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
        var (r, g, b) = HslColor.ToRgb(_hue, ns, nl);
        _preservingHue = true;
        SetRgb(r, g, b);
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
        var (r, g, b) = HslColor.ToRgb(_hue, s, l);
        SetRgb(r, g, b);
        SyncHsl();
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
        var (r, g, b) = HslColor.ToRgb(_hue, s, nl);
        _preservingHue = true;
        SetRgb(r, g, b);
        _preservingHue = false;
        _lightCursor.Position = LockCursor(pos, _lightCursor.Size, _lightBar.Size);
    }

    private void UpdateTint()
    {
        _preview.SetSlotTint(CharacterSlot.Hair, _r, _g, _b, _a);
        SyncHsl();
    }

    private void DyePressed()
    {
        GameManager.Instance.NetworkClient.Command($"/hairdye accept {_r} {_g} {_b} {_a}");
    }
}
