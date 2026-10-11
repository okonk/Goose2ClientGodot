using System.Globalization;
using Goose2Client.Network.Packets;

namespace Goose2Client.Overlays
{
    public enum BattleTextFamily
    {
        Red,
        Green,
        Yellow
    }

    public readonly record struct BattleTextMergeKey(BattleTextFamily Family, string Suffix);

    public static class BattleTextMergeRule
    {
        public static bool TryGet(BattleTextType type, string? text, out BattleTextMergeKey key, out long value)
        {
            key = default;
            value = 0;

            BattleTextFamily? family = type switch
            {
                BattleTextType.Red1 or BattleTextType.Red2 or BattleTextType.Red4 or BattleTextType.Red5 or BattleTextType.Red61 => BattleTextFamily.Red,
                BattleTextType.Green7 or BattleTextType.Green8 => BattleTextFamily.Green,
                BattleTextType.Yellow60 => BattleTextFamily.Yellow,
                _ => null
            };
            if (family is null || text is null)
            {
                return false;
            }

            int i = 0;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
            {
                i++;
            }

            int firstGroup = 0;
            int digits = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                {
                    digits++;
                    if (firstGroup < 3)
                    {
                        firstGroup++;
                    }
                    i++;
                    continue;
                }
                if ((c == ',' || c == ' ') && firstGroup >= 1 && firstGroup <= 3)
                {
                    int run = 0;
                    while (i + run + 1 < text.Length && text[i + run + 1] >= '0' && text[i + run + 1] <= '9')
                    {
                        run++;
                    }
                    if (run != 3)
                    {
                        break;
                    }
                    firstGroup = 3;
                    i += 4;
                    continue;
                }
                break;
            }

            if (digits == 0)
            {
                return false;
            }

            string digitText = text.Substring(0, i).Replace(",", "").Replace(" ", "");
            if (!long.TryParse(digitText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            string suffix = "";
            int j = i;
            if (j < text.Length)
            {
                if (!char.IsWhiteSpace(text[j]))
                {
                    return false;
                }
                suffix = text.Substring(j).Trim();
            }

            key = new BattleTextMergeKey(family.Value, suffix);
            return true;
        }

        public static string Format(BattleTextMergeKey key, long total)
        {
            string s = (total >= 0 ? "+" : "") + total.ToString("N0", CultureInfo.InvariantCulture);
            if (key.Suffix.Length > 0)
            {
                s += " " + key.Suffix;
            }
            return s;
        }
    }
}
