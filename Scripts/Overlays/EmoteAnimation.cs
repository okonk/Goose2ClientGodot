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
            var frames = GD.Load<SpriteFrames>(path);
            if (frames == null) return false;
            _sprite.SpriteFrames = frames;
            AddChild(_sprite);

            var clip = animationId.ToString();
            _sprite.Animation = clip;
            _sprite.Play(clip);

            // Plan: bottom-pivot — offset the sprite so its bottom edge sits at the node origin,
            // matching Unity's emote anchor point above the character.
            var firstFrame = _sprite.SpriteFrames.GetFrameTexture(clip, 0);
            if (firstFrame != null)
            {
                int height = (int)firstFrame.GetSize().Y;
                _sprite.Offset = new Vector2(0, -height / 2f);
            }

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
