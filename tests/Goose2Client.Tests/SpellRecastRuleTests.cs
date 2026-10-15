using System;
using Goose2Client;
using Xunit;

public class SpellRecastRuleTests
{
    private static SpellInfo Spell(SpellTargetType targetType) => new()
    {
        SlotNumber = 3,
        Name = "Test",
        TargetType = targetType,
        Cooldown = TimeSpan.FromSeconds(1),
    };

    [Fact] public void MonsterAndPlayerAreRecastTargets()
    {
        Assert.True(SpellRecastRule.IsRecastTarget(CharacterType.Monster));
        Assert.True(SpellRecastRule.IsRecastTarget(CharacterType.Player));
    }

    [Theory]
    [InlineData(CharacterType.Vendor)]
    [InlineData(CharacterType.Banker)]
    [InlineData(CharacterType.Quest)]
    [InlineData(CharacterType.Pet)]
    [InlineData(CharacterType.Ambient)]
    public void OtherNpcTypesAreNotRecastTargets(CharacterType type)
    {
        Assert.False(SpellRecastRule.IsRecastTarget(type));
        Assert.False(SpellRecastRule.CanRecast(Spell(SpellTargetType.NPC), type, spellOnCooldown: false));
    }

    [Theory]
    [InlineData(SpellTargetType.NPC)]
    [InlineData(SpellTargetType.NPCPlayer)]
    [InlineData(SpellTargetType.Player)]
    public void TargetableSpellsCanRecastOnCombatTargets(SpellTargetType targetType)
    {
        Assert.True(SpellRecastRule.CanRecast(Spell(targetType), CharacterType.Monster, spellOnCooldown: false));
        Assert.True(SpellRecastRule.CanRecast(Spell(targetType), CharacterType.Player, spellOnCooldown: false));
    }

    [Fact] public void NonTargetableSpellIsNotRecastable()
    {
        Assert.False(SpellRecastRule.IsTargetable(Spell(SpellTargetType.None)));
        Assert.False(SpellRecastRule.CanRecast(Spell(SpellTargetType.None), CharacterType.Monster, spellOnCooldown: false));
    }

    [Fact] public void MissingLastSpellIsNotRecastable()
    {
        Assert.False(SpellRecastRule.IsTargetable(null));
        Assert.False(SpellRecastRule.CanRecast(null, CharacterType.Monster, spellOnCooldown: false));
    }

    [Fact] public void CooldownBlocksRecast()
    {
        Assert.False(SpellRecastRule.CanRecast(Spell(SpellTargetType.NPC), CharacterType.Monster, spellOnCooldown: true));
    }
}
