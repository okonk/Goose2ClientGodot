namespace Goose2Client
{
    // Wire format: MFL<pvp>,<items>,<cast> — 1 = enabled (server: P.SendMapFlags).
    public readonly record struct MapFlags(bool PvPEnabled, bool ItemsEnabled, bool SpellsEnabled);

    public static class CurrentMapFlags
    {
        public static MapFlags Value { get; set; } = new(false, true, true);
    }
}
