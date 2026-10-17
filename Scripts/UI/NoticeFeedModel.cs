using System;
using System.Collections.Generic;
using Goose2Client;

namespace Goose2Client.UI
{
    public sealed class NoticeEntry
    {
        public NoticeEntry(NoticeType type, string text)
        {
            Type = type;
            Text = text;
        }

        public NoticeType Type { get; }
        public string Text { get; }
        public double Age { get; internal set; }
    }

    public sealed class NoticeFeedModel
    {
        public const int MaxEntries = 6;
        public const double Lifetime = 5.0;
        public const double FadeDuration = 1.0;

        private readonly List<NoticeEntry> _entries = new();

        public IReadOnlyList<NoticeEntry> Entries => _entries;

        public NoticeEntry Add(NoticeType type, string text)
        {
            var entry = new NoticeEntry(type, text);
            _entries.Add(entry);
            while (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
            return entry;
        }

        public void Tick(double delta)
        {
            for (int i = 0; i < _entries.Count; i++)
                _entries[i].Age += delta;
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Age >= Lifetime)
                    _entries.RemoveAt(i);
        }

        public static float Alpha(NoticeEntry e)
        {
            double t = (Lifetime - e.Age) / FadeDuration;
            return (float)Math.Clamp(t, 0.0, 1.0);
        }
    }
}
