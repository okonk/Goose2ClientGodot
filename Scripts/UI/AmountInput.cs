using System;
using System.Globalization;
using System.Linq;

namespace Goose2Client.UI;

public static class AmountInput
{
    public static long? Parse(string text, long min, long max)
    {
        var digits = new string(text.Where(c => c >= '0' && c <= '9').ToArray());
        if (digits.Length == 0) return null;
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > max)
            return max;
        return value < min ? min : value;
    }
}
