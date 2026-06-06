using System;

namespace Goose2Client
{
    public static partial class Helpers
    {
        public static string FormatDuration(this TimeSpan t)
        {
            string cd = "";
            if (t.Hours != 0)
                cd += t.Hours + "h ";

            if (t.Minutes != 0)
                cd += t.Minutes + "m ";

            var seconds = Math.Round(t.Seconds + t.Milliseconds / 1000.0f, 0);
            if (seconds != 0)
             cd += seconds + "s";

            return cd;
        }

        public static int StackSplit(int initialStack, bool ctrl, bool shift)
        {
            if (initialStack == 1) return 1;

            if (ctrl)
                return 1;
            else if (shift)
                return initialStack / 2;

            return initialStack;
        }
    }
}
