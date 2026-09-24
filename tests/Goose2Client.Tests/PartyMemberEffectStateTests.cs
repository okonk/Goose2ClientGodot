using Goose2Client;
using System.Linq;
using Xunit;

namespace Goose2Client.Tests
{
    public class PartyMemberEffectStateTests
    {
        private static void SeedVisible(PartyMemberEffectState s, int slot, int loginId)
        {
            Assert.True(s.AssignSlot(slot, loginId, alreadyVisible: true));
        }

        private static void Add(PartyMemberEffectState s, int loginId, int effectId, long nowMs, long remainingMs, long totalMs)
        {
            Assert.True(s.UpsertEffect(loginId, effectId, 1, 2, remainingMs, totalMs, $"E{effectId}", nowMs));
        }

        [Fact]
        public void AssignSlot_RejectsSlotBeyondConfiguredCount()
        {
            var s = new PartyMemberEffectState(maxSlots: 2);

            Assert.True(s.AssignSlot(1, 100));
            Assert.False(s.AssignSlot(2, 200));
            Assert.False(s.AssignSlot(-1, 200));
            Assert.False(s.IsVisible(200));
        }

        [Fact]
        public void AssignSlot_AssignsAndRejectsDuplicate()
        {
            var s = new PartyMemberEffectState();

            Assert.True(s.AssignSlot(0, 100));
            Assert.False(s.AssignSlot(0, 100));
        }

        [Fact]
        public void AssignSlot_ReplacementClearsPriorMemberState()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Add(s, 100, 1, 0, 5000, 10000);

            Assert.True(s.AssignSlot(0, 200));

            Assert.Empty(s.GetEffects(100));
            Assert.False(s.IsVisible(100));
            Assert.False(s.UpsertEffect(100, 2, 1, 2, 5000, 10000, "E2", 0));
        }

        [Fact]
        public void AssignSlot_CanSeedVisibilityFromGud()
        {
            var s = new PartyMemberEffectState();

            Assert.True(s.AssignSlot(1, 100, alreadyVisible: true));
            Assert.True(s.IsVisible(100));
        }

        [Fact]
        public void Visibility_EnterAndErasure()
        {
            var s = new PartyMemberEffectState();
            Assert.True(s.AssignSlot(0, 100));
            Assert.False(s.IsVisible(100));

            Assert.True(s.MarkVisible(100));
            Assert.True(s.IsVisible(100));
            Assert.False(s.MarkVisible(100));

            Assert.True(s.Erase(100));
            Assert.False(s.IsVisible(100));
            Assert.False(s.Erase(100));
        }

        [Fact]
        public void Upsert_RejectsUnknownMember()
        {
            var s = new PartyMemberEffectState();

            Assert.False(s.UpsertEffect(999, 1, 1, 2, 5000, 10000, "E1", 0));
            Assert.Empty(s.GetEffects(999));
        }

        [Fact]
        public void Upsert_RejectsCurrentButNonVisibleMember()
        {
            var s = new PartyMemberEffectState();
            Assert.True(s.AssignSlot(0, 100));

            Assert.False(s.UpsertEffect(100, 1, 1, 2, 5000, 10000, "E1", 0));
            Assert.Empty(s.GetEffects(100));
        }

        [Fact]
        public void Upsert_InsertsInOrder()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Add(s, 100, 1, 0, 5000, 10000);
            Add(s, 100, 2, 0, 5000, 10000);
            Add(s, 100, 3, 0, 5000, 10000);

            var effects = s.GetEffects(100);
            Assert.Equal(3, effects.Count);
            Assert.Equal(1, effects[0].EffectId);
            Assert.Equal(2, effects[1].EffectId);
            Assert.Equal(3, effects[2].EffectId);
        }

        [Fact]
        public void Upsert_SameIdRenewal_PreservesPositionAndReplacesDeadline()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Add(s, 100, 1, 1000, 5000, 10000);
            Add(s, 100, 2, 1000, 5000, 10000);
            Add(s, 100, 3, 1000, 5000, 10000);

            Assert.True(s.UpsertEffect(100, 1, 9, 8, 1000, 2000, "Renewed", 9000));

            var effects = s.GetEffects(100);
            Assert.Equal(3, effects.Count);
            Assert.Equal(1, effects[0].EffectId);
            Assert.Equal(2, effects[1].EffectId);
            Assert.Equal(3, effects[2].EffectId);
            Assert.Equal(10000, effects[0].ExpiresAt);
            Assert.Equal(9, effects[0].GraphicId);
            Assert.Equal("Renewed", effects[0].Name);
            Assert.Equal(6000, effects[1].ExpiresAt);
        }

        [Fact]
        public void Upsert_StoresDeadlineOnce_AndDoesNotRecomputeOnLaterReads()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Add(s, 100, 1, 1000, 5000, 10000);

            Add(s, 100, 2, 9000, 5000, 10000);

            var effects = s.GetEffects(100);
            Assert.Equal(6000, effects[0].ExpiresAt);
            Assert.Equal(14000, effects[1].ExpiresAt);
        }

        [Fact]
        public void Upsert_PermanentEffect_HasNoDeadline()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);

            Assert.True(s.UpsertEffect(100, 1, 1, 2, 0, 0, "Stasis", 0));

            var effect = Assert.Single(s.GetEffects(100));
            Assert.True(effect.IsPermanent);
            Assert.Equal(0, effect.ExpiresAt);
        }

        [Fact]
        public void Remove_SingleRemovalClosesGap()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Add(s, 100, 1, 0, 5000, 10000);
            Add(s, 100, 2, 0, 5000, 10000);
            Add(s, 100, 3, 0, 5000, 10000);

            Assert.True(s.RemoveEffect(100, 2));
            Assert.Equal(new[] { 1, 3 }, s.GetEffects(100).Select(e => e.EffectId).ToArray());

            Assert.True(s.RemoveEffect(100, 1));
            Assert.Equal(new[] { 3 }, s.GetEffects(100).Select(e => e.EffectId).ToArray());

            Assert.False(s.RemoveEffect(100, 2));
            Assert.False(s.RemoveEffect(100, 999));
        }

        [Fact]
        public void RemoveThenAdd_ReplacementDoesNotRetainOldEffect()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Add(s, 100, 1, 0, 5000, 10000);
            Add(s, 100, 2, 0, 5000, 10000);
            Add(s, 100, 3, 0, 5000, 10000);

            Assert.True(s.RemoveEffect(100, 1));
            Add(s, 100, 4, 0, 5000, 10000);

            Assert.Equal(new[] { 2, 3, 4 }, s.GetEffects(100).Select(e => e.EffectId).ToArray());
        }

        [Fact]
        public void ClearEffects_RemovesAllAndIsIdempotent()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Add(s, 100, 1, 0, 5000, 10000);
            Add(s, 100, 2, 0, 5000, 10000);

            Assert.True(s.ClearEffects(100));
            Assert.Empty(s.GetEffects(100));
            Assert.False(s.ClearEffects(100));
            Assert.True(s.IsVisible(100));
        }

        [Fact]
        public void Erase_ClearsEffectsAndBlocksLateUpsert()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Add(s, 100, 1, 0, 5000, 10000);

            Assert.True(s.Erase(100));
            Assert.Empty(s.GetEffects(100));

            Assert.False(s.UpsertEffect(100, 1, 1, 2, 5000, 10000, "E1", 100));
            Assert.Empty(s.GetEffects(100));
        }

        [Fact]
        public void ExpiredDeadline_NeverRemovesEntry()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Add(s, 100, 1, 0, 1000, 10000);

            Add(s, 100, 2, 10_000_000, 1000, 10000);

            var effects = s.GetEffects(100);
            Assert.Equal(2, effects.Count);
            Assert.Equal(1, effects[0].EffectId);
            Assert.Equal(1000, effects[0].ExpiresAt);
            Assert.False(effects[0].IsPermanent);
        }

        [Fact]
        public void RosterCompaction_PreservesStateOfShiftedMembers()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            SeedVisible(s, 1, 200);
            SeedVisible(s, 2, 300);
            Add(s, 100, 1, 0, 5000, 10000);
            Add(s, 200, 2, 0, 5000, 10000);
            Add(s, 300, 3, 0, 5000, 10000);

            Assert.True(s.AssignSlot(0, 200, alreadyVisible: true));
            Assert.True(s.AssignSlot(1, 300, alreadyVisible: true));
            Assert.True(s.ClearSlot(2));

            Assert.Equal(new[] { 2 }, s.GetEffects(200).Select(e => e.EffectId).ToArray());
            Assert.Equal(new[] { 3 }, s.GetEffects(300).Select(e => e.EffectId).ToArray());
            Assert.True(s.IsVisible(200));
            Assert.True(s.IsVisible(300));

            Assert.True(s.UpsertEffect(200, 4, 1, 2, 5000, 10000, "E4", 1000));
            Assert.Equal(new[] { 2, 4 }, s.GetEffects(200).Select(e => e.EffectId).ToArray());
        }

        [Fact]
        public void RosterCompaction_ClearsDepartedMemberState()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            SeedVisible(s, 1, 200);
            Add(s, 100, 1, 0, 5000, 10000);
            Add(s, 200, 2, 0, 5000, 10000);

            Assert.True(s.AssignSlot(0, 200, alreadyVisible: true));
            Assert.True(s.ClearSlot(1));

            Assert.Equal(new[] { 2 }, s.GetEffects(200).Select(e => e.EffectId).ToArray());
            Assert.True(s.IsVisible(200));
            Assert.True(s.UpsertEffect(200, 3, 1, 2, 5000, 10000, "E3", 1000));
            Assert.Equal(new[] { 2, 3 }, s.GetEffects(200).Select(e => e.EffectId).ToArray());

            Assert.Empty(s.GetEffects(100));
            Assert.False(s.IsVisible(100));
        }

        [Fact]
        public void ClearSlot_RemovesSlotMappingAndMemberState()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Add(s, 100, 1, 0, 5000, 10000);

            Assert.True(s.ClearSlot(0));
            Assert.False(s.ClearSlot(0));
            Assert.False(s.ClearSlot(5));
            Assert.Empty(s.GetEffects(100));
            Assert.False(s.IsVisible(100));
            Assert.False(s.UpsertEffect(100, 1, 1, 2, 5000, 10000, "E1", 0));
        }

        [Fact]
        public void ClearSlot_AllowsReassignmentWithoutPhantomMember()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Assert.True(s.ClearSlot(0));

            Assert.True(s.AssignSlot(0, 200));
            Assert.False(s.IsVisible(100));
            Assert.Empty(s.GetEffects(100));
            Assert.True(s.MarkVisible(200));
            Assert.True(s.UpsertEffect(200, 1, 1, 2, 5000, 10000, "E1", 0));
        }

        [Fact]
        public void ErasedPermanentEffect_IsAlsoBlocked()
        {
            var s = new PartyMemberEffectState();
            SeedVisible(s, 0, 100);
            Assert.True(s.UpsertEffect(100, 1, 1, 2, 0, 0, "Stasis", 0));

            Assert.True(s.Erase(100));
            Assert.False(s.UpsertEffect(100, 1, 1, 2, 0, 0, "Stasis", 100));
            Assert.Empty(s.GetEffects(100));
        }
    }
}
