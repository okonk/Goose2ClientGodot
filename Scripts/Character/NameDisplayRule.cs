namespace Goose2Client.Character
{
    public enum NameDisplayMode { Always = 0, PlayersOnly = 1, Never = 2 }

    public static class NameDisplayRule
    {
        public static bool ShouldRenderNameOverhead(NameDisplayMode mode, CharacterType type, bool serverHidden)
            => !serverHidden && mode switch
            {
                NameDisplayMode.Always => true,
                NameDisplayMode.PlayersOnly => type == CharacterType.Player,
                _ => false,
            };

        public static bool ShouldShowNameTooltip(bool visibleToViewer, bool roofOccluded, bool overheadShown)
            => visibleToViewer && !roofOccluded && !overheadShown;
    }
}
