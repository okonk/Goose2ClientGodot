using System.Collections.Generic;
using Godot;

namespace Goose2Client
{
    // Workaround for godotengine/godot#112067: a transient managed wrapper for a cached native
    // resource gets GC'd, its GCHandle released, and the next native->managed marshal of the same
    // object throws "Handle is not initialized". Holding the wrappers alive prevents that.
    public static class FrameTextureCache
    {
        private static readonly Dictionary<SpriteFrames, Dictionary<(StringName, int), Texture2D?>> Cache = new();

        public static Texture2D? Get(SpriteFrames frames, StringName anim, int frame)
        {
            if (!Cache.TryGetValue(frames, out var byFrame))
                Cache[frames] = byFrame = new Dictionary<(StringName, int), Texture2D?>();
            var key = (anim, frame);
            if (!byFrame.TryGetValue(key, out var tex) || !GodotObject.IsInstanceValid(tex))
                byFrame[key] = tex = frames.GetFrameTexture(anim, frame);
            return tex;
        }
    }
}
