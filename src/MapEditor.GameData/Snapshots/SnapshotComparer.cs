using System;
using System.Collections.Generic;
using MapEditor.GameData.Rows;

namespace MapEditor.GameData.Snapshots;

public static class SnapshotComparer
{
    public static bool Equals(IEnumerable<NpcSpawnRow> left, IEnumerable<NpcSpawnRow> right)
        => MultisetEquals(left, right);

    public static bool Equals(IEnumerable<WarpRow> left, IEnumerable<WarpRow> right)
        => MultisetEquals(left, right);

    internal static int Hash<T>(IEnumerable<T> rows) where T : notnull
    {
        int count = 0;
        long sum = 0;
        int xor = 0;

        foreach (var row in rows)
        {
            var h = row.GetHashCode();
            count++;
            sum += h;
            xor ^= h;
        }

        return HashCode.Combine(count, sum, xor);
    }

    private static bool MultisetEquals<T>(IEnumerable<T> left, IEnumerable<T> right) where T : notnull
    {
        var counts = new Dictionary<T, int>();

        foreach (var item in left)
            counts[item] = counts.GetValueOrDefault(item) + 1;

        foreach (var item in right)
        {
            if (!counts.TryGetValue(item, out var count) || count == 0)
                return false;

            counts[item] = count - 1;
        }

        foreach (var count in counts.Values)
        {
            if (count != 0)
                return false;
        }

        return true;
    }
}
