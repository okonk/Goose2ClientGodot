namespace Goose2Client.UI;

/// <summary>Pure visibility rule for the optional spirit bar.
/// Design: docs/plans/2026-07-24-spirit-bar-design.md.
/// Hidden until the first SNF arrives; option off → hidden. Otherwise shown once
/// SP has ever been non-zero (latch, persisted per character) or the current
/// MaxSP is non-zero.</summary>
public static class SpiritBarVisibility
{
    public static bool ShouldShow(bool snfReceived, bool optionOn, bool latched, long maxSp)
        => snfReceived && optionOn && (latched || maxSp > 0);
}
