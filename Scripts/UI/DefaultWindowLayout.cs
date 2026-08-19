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

    public static Vector2 For(string windowName)
        => windowName != null && Defaults.TryGetValue(windowName, out var p) ? p : new Vector2(100, 100);
}
