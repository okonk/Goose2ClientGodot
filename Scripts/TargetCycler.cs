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
            (int x, int y) player, int playerLoginId, int mapWidth, (int x, int y) viewRange,
            SpellTargetType type, bool filteringEnabled, bool searchDown)
        {
            var filtered = all.ToList();
            
            if (filteringEnabled)
            {
                var pvpEnabled = CurrentMapFlags.Value.PvPEnabled;
                filtered = filtered.Where(c =>
                {
                    if (c.LoginId == playerLoginId) return true;
                    var playerSide = c.Type is CharacterType.Player or CharacterType.Pet;
                    if (type == SpellTargetType.Player) return playerSide;
                    if (type == SpellTargetType.NPC) return !playerSide;
                    if (type == SpellTargetType.NPCPlayer) return !playerSide || pvpEnabled;
                    return true;
                }).ToList();
            }
            
            // Filter by view window
            filtered = filtered.Where(c => 
                System.Math.Abs(c.X - player.x) <= viewRange.x && 
                System.Math.Abs(c.Y - player.y) <= viewRange.y).ToList();
            
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
            // When current is not in the filtered list (idx == -1) but is non-null (stale target:
            // out of view or filtered out), search from the stale target's position key — nearest
            // candidate in the search direction, wrapping to the far end if none — matching the
            // legacy client. Only a null current jumps straight to first/last.
            if (idx == -1)
            {
                if (current == null)
                {
                    idx = searchDown ? 0 : filtered.Count - 1;
                }
                else
                {
                    int currentKey = current.Value.Y * mapWidth + current.Value.X;
                    if (searchDown)
                    {
                        idx = 0;
                        while (idx < filtered.Count && filtered[idx].Y * mapWidth + filtered[idx].X <= currentKey) idx++;
                        if (idx == filtered.Count) idx = 0;
                    }
                    else
                    {
                        idx = filtered.Count - 1;
                        while (idx >= 0 && filtered[idx].Y * mapWidth + filtered[idx].X >= currentKey) idx--;
                        if (idx < 0) idx = filtered.Count - 1;
                    }
                }
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
