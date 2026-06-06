using System;
using Xunit;

namespace Goose2Client.Tests
{
    public class SpellCooldownManagerTests
    {
        [Fact]
        public void GetCooldownRemaining_BeforeCast_ReturnsZero()
        {
            var manager = new SpellCooldownManager();
            var spell = new SpellInfo { SlotNumber = 1, Cooldown = TimeSpan.FromHours(1) };

            var remaining = manager.GetCooldownRemaining(spell);

            Assert.Equal(TimeSpan.Zero, remaining);
        }

        [Fact]
        public void GetCooldownRemaining_AfterCast_ReturnsNearFullCooldown()
        {
            var manager = new SpellCooldownManager();
            var spell = new SpellInfo { SlotNumber = 1, Cooldown = TimeSpan.FromHours(1) };

            manager.Cast(1);

            var remaining = manager.GetCooldownRemaining(spell);

            Assert.True(remaining > TimeSpan.FromMinutes(59));
        }

        [Fact]
        public void GetCooldownRemaining_AfterClear_ReturnsZero()
        {
            var manager = new SpellCooldownManager();
            var spell = new SpellInfo { SlotNumber = 1, Cooldown = TimeSpan.FromHours(1) };

            manager.Cast(1);
            manager.Clear(1);

            var remaining = manager.GetCooldownRemaining(spell);

            Assert.Equal(TimeSpan.Zero, remaining);
        }

        [Fact]
        public void Swap_MovesCastTimeFromOneSlotToAnother()
        {
            var manager = new SpellCooldownManager();
            var spell1 = new SpellInfo { SlotNumber = 1, Cooldown = TimeSpan.FromHours(1) };
            var spell2 = new SpellInfo { SlotNumber = 2, Cooldown = TimeSpan.FromHours(1) };

            manager.Cast(1);
            manager.Swap(1, 2);

            var remaining1 = manager.GetCooldownRemaining(spell1);
            var remaining2 = manager.GetCooldownRemaining(spell2);

            Assert.Equal(TimeSpan.Zero, remaining1);
            Assert.True(remaining2 > TimeSpan.FromMinutes(59));
        }
    }
}
