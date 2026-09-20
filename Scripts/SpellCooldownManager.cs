using System;
using System.Collections.Generic;

namespace Goose2Client
{
    public class SpellCooldownManager
    {
        private Dictionary<int, DateTimeOffset> nextCastableTimes = new Dictionary<int, DateTimeOffset>();

        public TimeSpan GetCooldownRemaining(SpellInfo spell)
        {
            if (!nextCastableTimes.TryGetValue(spell.SlotNumber, out var nextCastable))
                return TimeSpan.Zero;

            if (nextCastable <= DateTimeOffset.UtcNow)
                return TimeSpan.Zero;

            return nextCastable - DateTimeOffset.UtcNow;
        }

        public void Swap(int slot1, int slot2)
        {
            bool hasSlot1 = nextCastableTimes.TryGetValue(slot1, out var slot1NextCastable);
            bool hasSlot2 = nextCastableTimes.TryGetValue(slot2, out var slot2NextCastable);

            if (hasSlot2)
                nextCastableTimes[slot1] = slot2NextCastable;
            else
                Clear(slot1);

            if (hasSlot1)
                nextCastableTimes[slot2] = slot1NextCastable;
            else
                Clear(slot2);
        }

        public void Cast(int slot, TimeSpan cooldown)
        {
            nextCastableTimes[slot] = DateTimeOffset.UtcNow + cooldown;
        }

        public void Sync(int slot, TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero)
                Clear(slot);
            else
                nextCastableTimes[slot] = DateTimeOffset.UtcNow + remaining;
        }

        public void Clear(int slot)
        {
            nextCastableTimes.Remove(slot);
        }
    }
}
