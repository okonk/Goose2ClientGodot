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
