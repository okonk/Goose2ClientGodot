using Godot;
using System.Collections.Generic;

namespace Goose2Client.UI;

/// <summary>First-run window positions (used when no saved CharacterSettings position exists).</summary>
public static class DefaultWindowLayout
{
    private static readonly Dictionary<string, Vector2> Defaults = new()
    {
        ["Inventory"] = new Vector2(900, 360),
        ["Character"] = new Vector2(380, 120),
        ["Spellbook"] = new Vector2(700, 120),
        // x=520 clears the chat window (bottom-left, ends at x=508); y=679 puts the hotbar's
        // bottom (679+36=715) on the same line as the chat window's bottom (5px above a 720p
        // viewport).
        ["Hotbar"]    = new Vector2(520, 679),
        ["Vendor"]    = new Vector2(300, 200),
        ["Bank"]      = new Vector2(300, 200),
        ["CombineBag"]= new Vector2(540, 220),
        ["Options"]   = new Vector2(460, 260),
    };

    /// <summary>
    /// Transient dialog windows that should open centered on first run instead of using
    /// their <see cref="For"/> default; once the user drags one (a position is saved), the
    /// normal edge-stick <see cref="WindowPlacement.Resolve"/> rule takes over. Note the
    /// middle-band rule intentionally also moves the first-run Spellbook (default 700,120) off
    /// the right edge at larger canvases — it is top-center at 720p, so keeping it put matches
    /// the hotbar rationale.
    /// </summary>
    public static bool IsDialog(string windowName) => windowName is "Quest" or "Vendor" or "Info" or "Bank" or "CombineBag";

    public static Vector2 For(string windowName)
        => windowName != null && Defaults.TryGetValue(windowName, out var p) ? p : new Vector2(100, 100);
}
