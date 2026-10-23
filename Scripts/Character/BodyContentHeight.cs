using System.Collections.Generic;
using Godot;

namespace Goose2Client.Character
{
    public static class BodyContentHeight
    {
        private static readonly Dictionary<(SpriteFrames, StringName, int), int> Cache = new();

        public static int Get(SpriteFrames? frames, StringName clip, int anchorHeight)
        {
            if (frames == null || !frames.HasAnimation(clip)) return 0;
            var key = (frames, clip, anchorHeight);
            if (Cache.TryGetValue(key, out int cached)) return cached;

            int best = 0;
            for (int i = 0; i < frames.GetFrameCount(clip); i++)
            {
                if (FrameTextureCache.Get(frames, clip, i) is not AtlasTexture { Atlas: { } atlas } frame) continue;
                var region = (Rect2I)frame.Region;
                var used = atlas.GetImage()?.GetRegion(region).GetUsedRect() ?? default;
                if (used.Size.Y <= 0) continue;
                best = Mathf.Max(best, CharacterAnchor.ContentHeight(anchorHeight, region.Size.Y, used.Position.Y));
            }
            Cache[key] = best;
            return best;
        }
    }
}
