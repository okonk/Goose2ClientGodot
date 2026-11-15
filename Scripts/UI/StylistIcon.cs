using System.Collections.Generic;
using Godot;
using Goose2Client;
using Goose2Client.Character;

namespace Goose2Client.UI
{
    public static class StylistIcon
    {
        private static readonly Dictionary<(CharacterSlot, int), Texture2D?> Cache = new();
        private static readonly Dictionary<int, Texture2D?> BodyCache = new();

        public static Texture2D? Build(CharacterSlot slot, int id)
        {
            var key = (slot, id);
            if (Cache.TryGetValue(key, out var cached))
                return cached;

            var tex = IdleFrame(slot, id);
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

        public static Texture2D? BuildBody(int bodyId)
        {
            if (BodyCache.TryGetValue(bodyId, out var cached))
                return cached;

            var layers = new List<(Image Image, Vector2I Origin)>();
            foreach (var (slot, id) in new[]
            {
                (CharacterSlot.Body, bodyId),
                (CharacterSlot.Legs, CharacterLayout.UnderwearLegs(bodyId, 0)),
                (CharacterSlot.Chest, CharacterLayout.UnderwearChest(bodyId, 0))
            })
            {
                if (id <= 0) continue;
                var image = IdleFrame(slot, id)?.GetImage();
                if (image == null) continue;
                image.Convert(Image.Format.Rgba8);
                var size = image.GetSize();
                layers.Add((image, new Vector2I(-size.X / 2, CharacterAnchor.OffsetY(size.Y) - size.Y / 2)));
            }
            if (layers.Count == 0)
                return BodyCache[bodyId] = null;

            var min = layers[0].Origin;
            var max = layers[0].Origin + layers[0].Image.GetSize();
            foreach (var (image, origin) in layers)
            {
                min = min.Min(origin);
                max = max.Max(origin + image.GetSize());
            }

            var canvas = Image.CreateEmpty(max.X - min.X, max.Y - min.Y, false, Image.Format.Rgba8);
            foreach (var (image, origin) in layers)
                canvas.BlendRect(image, new Rect2I(Vector2I.Zero, image.GetSize()), origin - min);

            var used = canvas.GetUsedRect();
            if (used.Size != Vector2I.Zero)
                canvas = canvas.GetRegion(used);
            return BodyCache[bodyId] = ImageTexture.CreateFromImage(canvas);
        }

        private static Texture2D? IdleFrame(CharacterSlot slot, int id)
        {
            var path = $"res://Assets/Sprites/{CharacterLayout.TypeFolder(slot)}/{id}/animations.tres";
            if (!ResourceLoader.Exists(path))
                return null;

            var frames = GD.Load<SpriteFrames>(path);
            foreach (var cand in AnimationNames.Candidates("idle", 3, Direction.Down))
            {
                if (frames.HasAnimation(cand) && frames.GetFrameCount(cand) > 0)
                    return FrameTextureCache.Get(frames, cand, 0);
            }
            return null;
        }
    }
}
