namespace Goose2Client.Character
{
    public enum InvisibilityState { Normal, Translucent, Hidden }

    public static class InvisibilityRule
    {
        public static InvisibilityState Evaluate(bool isInvisible, bool canSeeInvisible, bool isLocalPlayer)
            => isInvisible
                ? (isLocalPlayer || canSeeInvisible ? InvisibilityState.Translucent : InvisibilityState.Hidden)
                : InvisibilityState.Normal;
    }
}
