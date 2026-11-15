namespace Goose2Client.Character
{
    public static class RespawnCountdown
    {
        public static ulong Deadline(ulong nowMsec, int seconds)
            => nowMsec + (ulong)System.Math.Max(0, seconds) * 1000UL;

        public static int SecondsLeft(ulong deadlineMsec, ulong nowMsec)
            => deadlineMsec <= nowMsec ? 0 : (int)((deadlineMsec - nowMsec + 999UL) / 1000UL);

        public static string Format(int seconds)
        {
            int s = System.Math.Max(0, seconds);
            return $"{s / 60}:{s % 60:00}";
        }

        public static string NameLabel(string name, int seconds) => $"{name} ({Format(seconds)})";
    }
}
