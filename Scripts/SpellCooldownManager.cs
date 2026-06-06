using System;
using System.Collections.Generic;

namespace Goose2Client
{
    public class SpellCooldownManager
    {
        private Dictionary<int, DateTimeOffset> lastCastTimes = new Dictionary<int, DateTimeOffset>();

        public TimeSpan GetCooldownRemaining(SpellInfo spell)
        {
            if (!lastCastTimes.TryGetValue(spell.SlotNumber, out var lastCast))
                return TimeSpan.Zero;

            var nextCast = lastCast + spell.Cooldown;
            if (nextCast <= DateTimeOffset.UtcNow)
                return TimeSpan.Zero;

            return nextCast - DateTimeOffset.UtcNow;
        }

        public void Swap(int slot1, int slot2)
        {
            bool hasSlot1 = lastCastTimes.TryGetValue(slot1, out var slot1LastCast);
            bool hasSlot2 = lastCastTimes.TryGetValue(slot2, out var slot2LastCast);

            if (hasSlot2)
                lastCastTimes[slot1] = slot2LastCast;
            else
                Clear(slot1);

            if (hasSlot1)
                lastCastTimes[slot2] = slot1LastCast;
            else
                Clear(slot2);
        }

        public void Cast(int slot)
        {
            lastCastTimes[slot] = DateTimeOffset.UtcNow;
        }

        public void Clear(int slot)
        {
            lastCastTimes.Remove(slot);
        }
    }
}
