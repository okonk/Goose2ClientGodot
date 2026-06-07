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

            // AnimatedSprite2D is Centered, so the texture is already centered on this node's
            // origin. The caller anchors the node on the target's center (tile cell / character),
            // so no extra vertical offset is needed. (Unity's -h/2 offset existed only to center
            // its BOTTOM-pivot sprite; re-applying it here double-shifted the effect downward.)

            return true;
        }
    }
}
