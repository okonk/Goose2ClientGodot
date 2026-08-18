using System.Collections.Generic;
using System.IO;

namespace Goose2Client.Character
{
    /// <summary>Port of Unity AnimationManager height lookup. Maps "{Type}-{Id}-{clip}" -> max
    /// frame height in px; defaults to 64 when absent (Unity GetHeight default).</summary>
    public class AnimationHeights
    {
        private readonly Dictionary<string, int> _heights;

        private AnimationHeights(Dictionary<string, int> heights) => _heights = heights;

        public static AnimationHeights Load(string path) => Parse(File.ReadAllLines(path));

        public static AnimationHeights Parse(string text) =>
            Parse(text.Split('\n'));

        public static AnimationHeights Parse(IEnumerable<string> lines)
        {
            var dict = new Dictionary<string, int>();
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;
                int comma = line.LastIndexOf(',');
                if (comma <= 0) continue;
                if (int.TryParse(line[(comma + 1)..], out int h))
                    dict[line[..comma]] = h;
            }
            return new AnimationHeights(dict);
        }

        public int GetHeight(string name) => _heights.TryGetValue(name, out int h) ? h : 64;
    }
}
