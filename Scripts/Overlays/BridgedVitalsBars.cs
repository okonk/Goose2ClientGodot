using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>Overhead HP/MP bars on the WorldTextBridge, so battle text cannot cover them.
    /// Position/Visible are bridge-owned — never set here.</summary>
    public partial class BridgedVitalsBars : Control, IBridgedText
    {
        private const float BarWidth = 32f;
        private const float HpBarHeight = 3f;
        private const float MpBarHeight = 2f;
        private const double BarTweenSeconds = 0.25;

        // MP bar fill color, matching the Unity CharacterHealthBars prefab (RGB 32,60,128).
        private static readonly Color MpColor = new(0.1254902f, 0.23529412f, 0.5019608f);

        private readonly ColorRect _background = new() { Color = new Color(0, 0, 0) };
        private readonly ColorRect _hp = new() { Color = GameColors.HpGreen };
        private readonly ColorRect _mp = new() { Color = MpColor };
        private float _worldScale = 1f;
        private float _hpPercent = 1f;
        private float _mpPercent = 1f;
        private Color _hpColor = GameColors.HpGreen;
        private Tween? _hpTween;
        private Tween? _mpTween;
        private bool _seeded;

        public Character.Character AnchorOwner { get; set; } = null!;
        public Vector2 LocalOffsetWorld { get; set; }
        public Rect2 ScreenBounds => new Rect2(Vector2.Zero, Size);

        public BridgedVitalsBars()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(_background);
            AddChild(_hp);
            AddChild(_mp);
        }

        public void Layout(Character.Character owner)
        {
            if (owner == null || !GodotObject.IsInstanceValid(owner)) return;
            int bodyHeight = owner.Height <= 0 ? 48 : owner.Height;
            LocalOffsetWorld = new Vector2(-BarWidth / 2f, -(bodyHeight + 8));
        }

        public void SetVitals(float hpPercent, float mpPercent, Color hpColor)
        {
            _hpPercent = hpPercent;
            _mpPercent = mpPercent;
            _hpColor = hpColor;
            ApplyFill(instant: !_seeded);
            _seeded = true;
        }

        public void ApplyScale(float textScale, float worldScale)
        {
            _worldScale = worldScale;
            Size = new Vector2(BarWidth, HpBarHeight + MpBarHeight) * worldScale;
            _background.Position = Vector2.Zero;
            _background.Size = new Vector2(BarWidth, HpBarHeight) * worldScale;
            _hp.Position = Vector2.Zero;
            _mp.Position = new Vector2(0, HpBarHeight * worldScale);
            ApplyFill(instant: true);
        }

        private void ApplyFill(bool instant)
        {
            Fill(_hp, ref _hpTween, _hpPercent, HpBarHeight, _hpColor, instant);
            Fill(_mp, ref _mpTween, _mpPercent, MpBarHeight, MpColor, instant);
        }

        /// Bars resize left-aligned (shrink from the right), matching the Unity CharacterHealthBar.
        private void Fill(ColorRect bar, ref Tween? tween, float percent, float height, Color color, bool instant)
        {
            if (tween != null && tween.IsValid())
                tween.Kill();

            var size = new Vector2(BarWidth * Mathf.Clamp(percent, 0f, 1f), height) * _worldScale;
            if (instant || bar.Size == size)
            {
                bar.Size = size;
                bar.Color = color;
                return;
            }

            tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(bar, "size", size, BarTweenSeconds);
            tween.TweenProperty(bar, "color", color, BarTweenSeconds);
        }
    }
}
