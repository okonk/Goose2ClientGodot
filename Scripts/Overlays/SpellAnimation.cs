using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>Looping spell impact effect. Self-frees when clip length elapses.
    /// Spells stack (no replacement) and are positioned by the caller.</summary>
    public partial class SpellAnimation : WorldOverlay
    {
        public bool Setup(int animationId)
        {
            var path = $"res://Assets/Sprites/Effects/{animationId}/animations.tres";
            if (!ResourceLoader.Exists(path)) return false;

            var sprite = new AnimatedSprite2D
            {
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            };
            var frames = GD.Load<SpriteFrames>(path);
            if (frames == null) return false;
            sprite.SpriteFrames = frames;
            AddChild(sprite);

            var clip = animationId.ToString();
            sprite.Animation = clip;
            sprite.Play(clip);

            // Compute clip length (do NOT mutate the shared cached resource).
            double speed = sprite.SpriteFrames.GetAnimationSpeed(clip);
            int n = sprite.SpriteFrames.GetFrameCount(clip);
            double total = 0;
            for (int i = 0; i < n; i++)
                total += sprite.SpriteFrames.GetFrameDuration(clip, i);
            double lengthSeconds = speed > 0 ? total / speed : 0.5;

            Lifetime = new OverlayLifetime(lengthSeconds);

            // Compute effect height = max frame texture height, fallback 64.
            int height = 0;
            for (int i = 0; i < n; i++)
            {
                var tex = sprite.SpriteFrames.GetFrameTexture(clip, i);
                if (tex != null)
                {
                    int h = (int)tex.GetSize().Y;
                    if (h > height) height = h;
                }
            }
            if (height == 0) height = 64;

            // Apply the spell vertical offset to the INNER sprite (so the node's own Position
            // stays the pure anchor the caller sets).
            // Unity yOffset is +Y-up; negate for Godot +Y-down. Vertical placement eye-verified in live E2E (Task 10).
            sprite.Position = new Vector2(0, -SpellLayout.VerticalOffsetPixels(height));

            return true;
        }
    }
}
