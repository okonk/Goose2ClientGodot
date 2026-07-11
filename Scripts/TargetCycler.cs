using System.Collections.Generic;
using System.Linq;

namespace Goose2Client
{
    public readonly record struct TargetCandidate(int LoginId, int X, int Y, CharacterType Type);
    
    public static class TargetCycler
    {
        public const int ViewRangeX = 10, ViewRangeY = 8;
        
        public static TargetCandidate? Next(
            IEnumerable<TargetCandidate> all, TargetCandidate? current,
            (int x, int y) player, int mapWidth, SpellTargetType type, 
            bool filteringEnabled, bool searchDown)
        {
            var filtered = all.ToList();
            
            // Filter by target type (skip filter for NPCPlayer which accepts everything)
            if (filteringEnabled && type != SpellTargetType.NPCPlayer)
            {
                filtered = filtered.Where(c =>
                {
                    if (type == SpellTargetType.Player) return c.Type == CharacterType.Player;
                    if (type == SpellTargetType.NPC) return c.Type != CharacterType.Player;
                    return true;
                }).ToList();
            }
            
            // Filter by view window
            filtered = filtered.Where(c => 
                System.Math.Abs(c.X - player.x) <= ViewRangeX && 
                System.Math.Abs(c.Y - player.y) <= ViewRangeY).ToList();
            
            if (!filtered.Any()) return null;
            
            // Sort by position key: Y * mapWidth + X
            filtered = filtered.OrderBy(c => c.Y * mapWidth + c.X).ToList();
            
            // Find current index
            int idx = -1;
            if (current != null)
            {
                for (int i = 0; i < filtered.Count; i++)
                {
                    if (filtered[i].LoginId == current.Value.LoginId)
                    { idx = i; break; }
                }
            }
            
            // Move to next (or previous) with wrap.
            // When current is not in the filtered list (idx == -1), forward goes to first,
            // backward goes to last — avoiding the off-by-one where (-1-1+n)%n picks n-2.
            if (idx == -1)
            {
                idx = searchDown ? 0 : filtered.Count - 1;
            }
            else if (searchDown)
            {
                idx = (idx + 1) % filtered.Count;
            }
            else
            {
                idx = (idx - 1 + filtered.Count) % filtered.Count;
            }
            
            return filtered[idx];
        }
    }
}
