using System.Collections.Generic;
using System.Linq;

namespace Goose2Client.Map
{
    public sealed class ZoneBook
    {
        private readonly Dictionary<int, ulong> _expiresAt = new();

        public bool Add(int id, ulong nowMs, long remainingMs)
        {
            bool isNew = !_expiresAt.ContainsKey(id);
            _expiresAt[id] = nowMs + (ulong)System.Math.Max(0, remainingMs);
            return isNew;
        }

        public bool Remove(int id) => _expiresAt.Remove(id);

        public List<int> TakeExpired(ulong nowMs)
        {
            var expired = _expiresAt.Where(kv => kv.Value <= nowMs).Select(kv => kv.Key).ToList();
            foreach (var id in expired) _expiresAt.Remove(id);
            return expired;
        }
    }
}
