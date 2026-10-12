namespace Goose2Client
{
    public static class SpellRecastRule
    {
        public static bool IsTargetable(SpellInfo? spell)
            => spell is { TargetType: not SpellTargetType.None };

        // Vendor, banker and quest NPCs keep their right-click window; only combat targets recast.
        public static bool IsRecastTarget(CharacterType type)
            => type is CharacterType.Monster or CharacterType.Player;

        public static bool CanRecast(SpellInfo? lastSpell, CharacterType clickedType, bool spellOnCooldown)
            => IsTargetable(lastSpell) && IsRecastTarget(clickedType) && !spellOnCooldown;
    }
}
