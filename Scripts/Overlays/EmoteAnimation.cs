using Godot;

namespace Goose2Client.Overlays
{
    /// <summary>Looping emote effect rendered above a character. Self-frees when clip length elapses.</summary>
    public partial class EmoteAnimation : WorldOverlay
    {
        private AnimatedSprite2D _sprite;

        public bool Setup(int animationId)
        {
            var path = $"res://Assets/Sprites/Effects/{animationId}/animations.tres";
            if (!ResourceLoader.Exists(path)) return false;

            _sprite = new AnimatedSprite2D
            {
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            };
            _sprite.SpriteFrames = GD.Load<SpriteFrames>(path);
            AddChild(_sprite);

            var clip = animationId.ToString();
            _sprite.Animation = clip;
            _sprite.Play(clip);

            // Compute clip length (do NOT mutate the shared cached resource).
            double speed = _sprite.SpriteFrames.GetAnimationSpeed(clip);
            int n = _sprite.SpriteFrames.GetFrameCount(clip);
            double total = 0;
            for (int i = 0; i < n; i++)
                total += _sprite.SpriteFrames.GetFrameDuration(clip, i);
            double lengthSeconds = speed > 0 ? total / speed : 0.5;

            Lifetime = new OverlayLifetime(lengthSeconds);
            return true;
        }
    }
}
