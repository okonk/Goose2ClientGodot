using System;
using System.Collections.Generic;
using Goose2Client.Logs;

namespace Goose2Client.Logs
{
    public enum LogFilterPreset
    {
        LastHour,
        Previous24Hours,
        Previous7Days,
        Previous30Days,
        Custom
    }

    public sealed class LogFilterDraft
    {
        public LogFilterPreset Preset { get; set; } = LogFilterPreset.Previous24Hours;
        public long StartUnixMs { get; set; }
        public long EndUnixMs { get; set; }
        public long? DefaultStartUnixMs { get; set; }
        public long? DefaultEndUnixMs { get; set; }
        public string StartText { get; set; } = "";
        public string EndText { get; set; } = "";
        public string Participant { get; set; } = "";
        public string MapText { get; set; } = "";
        public List<int> SelectedTypeIds { get; set; } = new();
        public string Text { get; set; } = "";

        public LogFilterDraft Clone()
        {
            return new LogFilterDraft
            {
                Preset = Preset,
                StartUnixMs = StartUnixMs,
                EndUnixMs = EndUnixMs,
                DefaultStartUnixMs = DefaultStartUnixMs,
                DefaultEndUnixMs = DefaultEndUnixMs,
                StartText = StartText,
                EndText = EndText,
                Participant = Participant,
                MapText = MapText,
                SelectedTypeIds = new List<int>(SelectedTypeIds),
                Text = Text
            };
        }
    }

    public sealed class LogFilterValidationResult
    {
        public bool Success { get; }
        public string? Error { get; }
        public LogFreshFilterSnapshot? Snapshot { get; }

        private LogFilterValidationResult(bool success, string? error, LogFreshFilterSnapshot? snapshot)
        {
            Success = success;
            Error = error;
            Snapshot = snapshot;
        }

        public static LogFilterValidationResult Ok(LogFreshFilterSnapshot snapshot) => new(true, null, snapshot);
        public static LogFilterValidationResult Fail(string error) => new(false, error, null);
    }

    public sealed class LogMetadataState
    {
        public int WindowId { get; private set; }
        public bool Malformed { get; private set; }
        public bool Complete { get; private set; }
        public long DefaultStartUnixMs { get; private set; }
        public long DefaultEndUnixMs { get; private set; }
        public IReadOnlyList<LogTypeMetadata> Types { get; private set; } = Array.Empty<LogTypeMetadata>();
        public IReadOnlyList<LogMapMetadata> Maps { get; private set; } = Array.Empty<LogMapMetadata>();

        private readonly List<LogTypeMetadata> _types = new();
        private readonly List<LogMapMetadata> _maps = new();

        public LogMetadataState(int windowId)
        {
            WindowId = windowId;
        }

        public bool FeedLmt(LogTypeMetadata packet)
        {
            if (packet == null || packet.WindowId != WindowId || Complete || Malformed)
                return false;
            if (!packet.IsValid)
            {
                Malformed = true;
                _types.Clear();
                Types = _types;
                return false;
            }
            if (!LogFilterValidator.IsKnownGroup(packet.Group))
            {
                Malformed = true;
                _types.Clear();
                Types = _types;
                return false;
            }
            for (int i = 0; i < _types.Count; i++)
            {
                if (_types[i].TypeId == packet.TypeId)
                {
                    Malformed = true;
                    _types.Clear();
                    Types = _types;
                    return false;
                }
            }
            _types.Add(packet);
            Types = _types;
            return true;
        }

        public bool FeedLmm(LogMapMetadata packet)
        {
            if (packet == null || packet.WindowId != WindowId || Complete || Malformed)
                return false;
            if (!packet.IsValid)
            {
                Malformed = true;
                _maps.Clear();
                Maps = _maps;
                return false;
            }
            for (int i = 0; i < _maps.Count; i++)
            {
                if (_maps[i].MapId == packet.MapId)
                {
                    Malformed = true;
                    _maps.Clear();
                    Maps = _maps;
                    return false;
                }
            }
            _maps.Add(packet);
            Maps = _maps;
            return true;
        }

        public bool FeedLmd(LogDefaultsMetadata packet)
        {
            if (packet == null || packet.WindowId != WindowId || Complete)
                return false;
            if (Malformed || !packet.IsValid)
                return false;
            DefaultStartUnixMs = packet.StartUnixMs;
            DefaultEndUnixMs = packet.EndUnixMs;
            Complete = true;
            return true;
        }
    }

    public enum LogQuickActionTarget
    {
        Type,
        Primary,
        Related
    }

    public sealed class LogQuickActions
    {
        public bool TypeAvailable { get; }
        public int TypeId { get; }
        public bool PrimaryAvailable { get; }
        public LogEntityKind PrimaryKind { get; }
        public int PrimaryId { get; }
        public bool RelatedAvailable { get; }
        public LogEntityKind RelatedKind { get; }
        public int RelatedId { get; }

        public LogQuickActions(bool typeAvailable, int typeId, bool primaryAvailable, LogEntityKind primaryKind, int primaryId, bool relatedAvailable, LogEntityKind relatedKind, int relatedId)
        {
            TypeAvailable = typeAvailable;
            TypeId = typeId;
            PrimaryAvailable = primaryAvailable;
            PrimaryKind = primaryKind;
            PrimaryId = primaryId;
            RelatedAvailable = relatedAvailable;
            RelatedKind = relatedKind;
            RelatedId = relatedId;
        }
    }

    public sealed class LogResponseResult
    {
        public IReadOnlyList<LogRow> Rows { get; }
        public bool HasMore { get; }
        public string CurrentToken { get; }
        public string NextToken { get; }

        public LogResponseResult(IReadOnlyList<LogRow> rows, bool hasMore, string currentToken, string nextToken)
        {
            Rows = rows;
            HasMore = hasMore;
            CurrentToken = currentToken;
            NextToken = nextToken;
        }
    }
}
