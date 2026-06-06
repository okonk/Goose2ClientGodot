using System;

namespace Goose2Client.UI;

/// <summary>
/// Pure paging helpers for the spellbook — no Godot dependencies.
/// Pages are uniform: page p holds global slot indices [p*slotsPerPage, (p+1)*slotsPerPage).
/// </summary>
public static class SpellbookPaging
{
    /// <summary>
    /// Page + slot-within-page for a global index; null if out of range.
    /// </summary>
    public static (int Page, int Slot)? Locate(int globalIndex, int slotsPerPage, int pageCount)
    {
        if (globalIndex < 0 || slotsPerPage <= 0) return null;
        int page = globalIndex / slotsPerPage;
        if (page >= pageCount) return null;
        return (page, globalIndex % slotsPerPage);
    }

    /// <summary>
    /// First EMPTY global slot index, scanning pages AFTER fromIndex's page (forward=true)
    /// or pages BEFORE it (forward=false).
    /// Returns null if none found.
    /// Stops at the FIRST empty slot — improvement over Unity which lacked a break and
    /// would issue redundant moves.
    /// </summary>
    public static int? FirstEmpty(int fromIndex, bool forward, int slotsPerPage, int pageCount, Func<int, bool> isOccupied)
    {
        int fromPage = fromIndex / slotsPerPage;
        int startPage = forward ? fromPage + 1 : 0;
        int endPage = forward ? pageCount : fromPage; // exclusive
        for (int p = startPage; p < endPage; p++)
            for (int j = 0; j < slotsPerPage; j++)
            {
                int global = p * slotsPerPage + j;
                if (!isOccupied(global)) return global;
            }
        return null;
    }
}
