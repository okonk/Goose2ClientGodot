namespace Goose2Client.Character
{
    public enum InvisibilityState { Normal, Translucent, Hidden }

    public static class InvisibilityRule
    {
        public static InvisibilityState Evaluate(bool isInvisible, bool canSeeInvisible, bool isLocalPlayer, bool isPartyMember)
            => isInvisible
                ? (isLocalPlayer || canSeeInvisible || isPartyMember ? InvisibilityState.Translucent : InvisibilityState.Hidden)
                : InvisibilityState.Normal;
    }
}
