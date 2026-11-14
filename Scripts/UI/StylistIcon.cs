using System.Collections.Generic;
using Godot;
using Goose2Client;
using Goose2Client.Character;

namespace Goose2Client.UI
{
    public static class StylistIcon
    {
        private static readonly Dictionary<(CharacterSlot, int), Texture2D?> Cache = new();

        public static Texture2D? Build(CharacterSlot slot, int id)
        {
            var key = (slot, id);
            if (Cache.TryGetValue(key, out var cached))
                return cached;

            var path = $"res://Assets/Sprites/{CharacterLayout.TypeFolder(slot)}/{id}/animations.tres";
            if (!ResourceLoader.Exists(path))
                return Cache[key] = null;

            var frames = GD.Load<SpriteFrames>(path);
            string? clip = null;
            foreach (var cand in AnimationNames.Candidates("idle", 3, Direction.Down))
            {
                if (frames.HasAnimation(cand) && frames.GetFrameCount(cand) > 0)
                {
                    clip = cand;
                    break;
                }
            }
            if (clip == null)
                return Cache[key] = null;

            var tex = FrameTextureCache.Get(frames, clip, 0);
            if (tex == null)
                return Cache[key] = null;

            var image = tex.GetImage();
            if (image != null)
            {
                var rect = image.GetUsedRect();
                if (rect.Size == Vector2I.Zero)
                    return Cache[key] = tex;
                return Cache[key] = ImageTexture.CreateFromImage(image.GetRegion(rect));
            }

            return Cache[key] = tex;
        }
    }
}
