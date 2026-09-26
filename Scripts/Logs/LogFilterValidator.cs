using System;
using System.Collections.Generic;
using System.Globalization;
using Goose2Client.Logs;
using Goose2Client.Network;

namespace Goose2Client.Logs
{
    public static class LogFilterValidator
    {
        private static readonly string[] KnownGroups =
        {
            "Communication", "Sessions/Security", "Social", "Items/Economy", "GM Actions", "Other/Retired"
        };

        private const int ParticipantMaxUtf8Bytes = 64;
        private const int TextMaxUtf8Bytes = 4096;
        private static readonly TimeSpan MaxSpan = TimeSpan.FromDays(31);
        private static readonly TimeSpan MaxTextSpan = TimeSpan.FromDays(7);
        private static readonly string[] CustomFormats = { "yyyy-MM-dd HH:mm:ss" };

        public static bool IsKnownGroup(string group)
        {
            foreach (string known in KnownGroups)
            {
                if (group == known)
                    return true;
            }
            return false;
        }

        public static void ApplyPreset(LogFilterDraft draft, DateTime utcNow)
        {
            long end = new DateTimeOffset(utcNow).ToUnixTimeMilliseconds();
            long start = end - (draft.Preset switch
            {
                LogFilterPreset.LastHour => 3600_000L,
                LogFilterPreset.Previous7Days => 7L * 86_400_000L,
                LogFilterPreset.Previous30Days => 30L * 86_400_000L,
                _ => 24L * 3600_000L
            });
            draft.StartUnixMs = start;
            draft.EndUnixMs = end;
        }

        public static void Clear(LogFilterDraft draft, DateTime utcNow)
        {
            draft.Preset = LogFilterPreset.Previous24Hours;
            ApplyPreset(draft, utcNow);
            draft.StartText = "";
            draft.EndText = "";
            draft.Participant = "";
            draft.MapText = "";
            draft.SelectedTypeIds = new List<int>();
            draft.Text = "";
        }

        public static LogFilterValidationResult Validate(LogFilterDraft draft, LogMetadataState metadata)
        {
            if (draft == null)
                return LogFilterValidationResult.Fail("draft is missing");
            long start;
            long end;
            if (draft.Preset == LogFilterPreset.Custom)
            {
                if (!DateTime.TryParseExact(draft.StartText, CustomFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.NoCurrentDateDefault, out DateTime startUtc)
                    || !DateTime.TryParseExact(draft.EndText, CustomFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.NoCurrentDateDefault, out DateTime endUtc))
                    return LogFilterValidationResult.Fail("custom range must use yyyy-MM-dd HH:mm:ss UTC");
                start = new DateTimeOffset(startUtc).ToUnixTimeMilliseconds();
                end = new DateTimeOffset(endUtc).ToUnixTimeMilliseconds();
                if (end <= start)
                    return LogFilterValidationResult.Fail("start must be before end");
                TimeSpan span = endUtc - startUtc;
                if (span > MaxSpan)
                    return LogFilterValidationResult.Fail("range exceeds 31 days");
                if (draft.Text.Length > 0 && span > MaxTextSpan)
                    return LogFilterValidationResult.Fail("text search range exceeds 7 days");
            }
            else
            {
                start = draft.StartUnixMs;
                end = draft.EndUnixMs;
                if (end <= start)
                    return LogFilterValidationResult.Fail("start must be before end");
            }

            string participant = draft.Participant ?? "";
            if (participant.Length > 0)
            {
                if (participant[0] == '#')
                {
                    if (!TryParsePositiveInt32(participant.Substring(1), out int participantId))
                        return LogFilterValidationResult.Fail("participant id must be a positive integer");
                    participant = "#" + participantId;
                }
                else if (participant[0] == ' ' || participant[participant.Length - 1] == ' ' || participant.IndexOf(' ') >= 0)
                {
                    return LogFilterValidationResult.Fail("participant is invalid");
                }
                if (!ProtocolTextCodec.TryEncode(participant, ParticipantMaxUtf8Bytes, out _))
                    return LogFilterValidationResult.Fail("participant exceeds limit");
            }

            int mapId = 0;
            string mapText = draft.MapText ?? "";
            if (mapText.Length > 0)
            {
                if (mapText[0] == '#')
                {
                    if (!TryParsePositiveInt32(mapText.Substring(1), out mapId))
                        return LogFilterValidationResult.Fail("map id must be a positive integer");
                }
                else
                {
                    bool matched = false;
                    for (int i = 0; i < metadata.Maps.Count; i++)
                    {
                        if (mapText == metadata.Maps[i].MapName + " (#" + metadata.Maps[i].MapId + ")")
                        {
                            mapId = metadata.Maps[i].MapId;
                            matched = true;
                            break;
                        }
                    }
                    if (!matched)
                        return LogFilterValidationResult.Fail("map is not selected");
                }
            }

            var typeIds = new List<int>();
            var seen = new HashSet<int>();
            foreach (int typeId in draft.SelectedTypeIds ?? new List<int>())
            {
                bool known = false;
                for (int i = 0; i < metadata.Types.Count; i++)
                {
                    if (metadata.Types[i].TypeId == typeId)
                    {
                        known = true;
                        break;
                    }
                }
                if (!known)
                    return LogFilterValidationResult.Fail("type is not available");
                if (seen.Add(typeId))
                    typeIds.Add(typeId);
            }
            typeIds.Sort();

            string text = draft.Text ?? "";
            if (!ProtocolTextCodec.TryEncode(text, TextMaxUtf8Bytes, out _))
                return LogFilterValidationResult.Fail("text exceeds limit");

            return LogFilterValidationResult.Ok(new LogFreshFilterSnapshot(start, end, participant, mapId, typeIds, text));
        }

        public static IReadOnlyList<string> MapSuggestions(LogMetadataState metadata, string prefix)
        {
            string text = prefix ?? "";
            var list = new List<string>();
            for (int i = 0; i < metadata.Maps.Count; i++)
            {
                string display = metadata.Maps[i].MapName + " (#" + metadata.Maps[i].MapId + ")";
                if (display.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    list.Add(display);
            }
            return list;
        }

        public static IReadOnlyList<string> TypeSuggestions(LogMetadataState metadata, string prefix)
        {
            string text = prefix ?? "";
            var list = new List<string>();
            for (int i = 0; i < metadata.Types.Count; i++)
            {
                if (metadata.Types[i].Label.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    list.Add(metadata.Types[i].Label);
            }
            return list;
        }

        private static bool TryParsePositiveInt32(string text, out int value)
        {
            value = 0;
            if (text.Length == 0)
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                    return false;
            }
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) || parsed <= 0 || parsed > int.MaxValue)
                return false;
            value = (int)parsed;
            return true;
        }
    }
}
