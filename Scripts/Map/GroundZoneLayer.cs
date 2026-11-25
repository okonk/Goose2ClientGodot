using System.Collections.Generic;
using Godot;
using Goose2Client.Network.Packets;
using Goose2Client.Overlays;

namespace Goose2Client.Map
{
    public partial class GroundZoneLayer : Node2D
    {
        private readonly ZoneBook _book = new();
        private readonly Dictionary<int, List<Node2D>> _sprites = new();
        private readonly PreviewNode _preview = new() { Name = "Preview" };

        public override void _Ready() => AddChild(_preview);

        public void ShowPreview(List<(int X, int Y)> tiles, Color color) => _preview.Set(tiles, color);

        public void HidePreview() => _preview.Set(new(), default);

        private sealed partial class PreviewNode : Node2D
        {
            private List<(int X, int Y)> _tiles = new();
            private Color _color;

            public void Set(List<(int X, int Y)> tiles, Color color)
            {
                _tiles = tiles;
                _color = color;
                QueueRedraw();
            }

            public override void _Draw()
            {
                var size = new Vector2(MapCoords.TileSize, MapCoords.TileSize);
                foreach (var (x, y) in _tiles)
                    DrawRect(new Rect2(MapCoords.TileCenter(x, y) - size / 2f, size), _color);
            }
        }

        public void Add(ZoneAddPacket p)
        {
            if (!_book.Add(p.Id, Time.GetTicksMsec(), p.RemainingMs))
                FreeSprites(p.Id);

            var sprites = new List<Node2D>();
            foreach (var (x, y) in p.Tiles)
            {
                var s = new SpellAnimation { Name = $"Zone{p.Id}({x},{y})" };
                AddChild(s);
                if (!s.Setup(p.AnimationId, new Vector2(0, -MapCoords.TileSize / 2f), loop: true)) { s.QueueFree(); continue; }
                s.Position = MapCoords.TileBottomCenter(x, y);
                sprites.Add(s);
            }
            _sprites[p.Id] = sprites;
        }

        public void Remove(int id)
        {
            _book.Remove(id);
            FreeSprites(id);
        }

        public override void _Process(double delta)
        {
            foreach (var id in _book.TakeExpired(Time.GetTicksMsec()))
                FreeSprites(id);
        }

        private void FreeSprites(int id)
        {
            if (!_sprites.Remove(id, out var sprites)) return;
            foreach (var s in sprites)
                if (GodotObject.IsInstanceValid(s)) s.QueueFree();
        }
    }
}
