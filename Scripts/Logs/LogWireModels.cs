using System;
using System.Collections.Generic;

namespace Goose2Client.Logs
{
    public enum LogOtherIdKind
    {
        Unused,
        Player,
        Guild,
        Item,
        NpcTemplate
    }

    public enum LogEntityKind
    {
        Player,
        Item,
        Guild,
        NpcTemplate,
        Map,
        StoredValue
    }

    public enum LogNavigationIntent
    {
        Next,
        Previous
    }

    public abstract record LogPacketData
    {
        public abstract bool IsValid { get; init; }
        public abstract int WindowId { get; init; }
        public abstract int RequestId { get; init; }
        public abstract int WireLength { get; init; }
    }

    public sealed record LogTypeMetadata(bool IsValid, int WindowId, int RequestId, int WireLength, int TypeId, string Group, string Label) : LogPacketData;

    public sealed record LogMapMetadata(bool IsValid, int WindowId, int RequestId, int WireLength, int MapId, string MapName) : LogPacketData;

    public sealed record LogDefaultsMetadata(bool IsValid, int WindowId, int RequestId, int WireLength, long StartUnixMs, long EndUnixMs) : LogPacketData;

    public sealed record LogResultBegin(bool IsValid, int WindowId, int RequestId, int WireLength) : LogPacketData;

    public sealed record LogResultData(bool IsValid, int WindowId, int RequestId, int WireLength, int RowOrdinal, int ChunkIndex, int ChunkCount, string Segment) : LogPacketData;

    public sealed record LogResultFinish(bool IsValid, int WindowId, int RequestId, int WireLength, bool HasMore, string CurrentPageToken, string NextPageToken) : LogPacketData;

    public sealed record LogResultError(bool IsValid, int WindowId, int RequestId, int WireLength, string SafeMessage) : LogPacketData;

    public sealed record LogRowEntity(string Label, LogEntityKind Kind, long? Id, string Name, bool CanQuickFilter);

    public sealed record LogRowMap(long Id, string Name, bool CanQuickFilter);

    public sealed record LogRowRaw(
        long PlayerId,
        bool PlayerIdIsInteger,
        long OtherId,
        bool OtherIdIsInteger,
        long MapId,
        bool MapIdIsInteger,
        long MapX,
        bool MapXIsInteger,
        long MapY,
        bool MapYIsInteger);

    public sealed record LogRow(
        long RowId,
        long UtcMilliseconds,
        long TypeId,
        bool TypeIsInteger,
        string EventLabel,
        string EventGroup,
        LogOtherIdKind OtherIdKind,
        LogRowEntity Primary,
        LogRowEntity? Related,
        LogRowMap? Map,
        LogRowRaw Raw,
        string Summary,
        string OriginalText);

    public sealed record LogFreshFilterSnapshot(
        long StartUnixMs,
        long EndUnixMs,
        string Participant,
        int MapId,
        IReadOnlyList<int> TypeIds,
        string Text);

    public sealed class LogQuerySubmission
    {
        public sealed record Fresh
        {
            public int WindowId { get; }
            public int RequestId { get; }
            public LogFreshFilterSnapshot Filter { get; }

            public Fresh(int windowId, int requestId, LogFreshFilterSnapshot filter)
            {
                if (windowId <= 0)
                    throw new ArgumentOutOfRangeException(nameof(windowId));
                if (requestId <= 0)
                    throw new ArgumentOutOfRangeException(nameof(requestId));
                Filter = filter ?? throw new ArgumentNullException(nameof(filter));
                WindowId = windowId;
                RequestId = requestId;
            }
        }

        public sealed record Page
        {
            public int WindowId { get; }
            public int RequestId { get; }
            public string PageToken { get; }
            public LogNavigationIntent Intent { get; }

            public Page(int windowId, int requestId, string pageToken, LogNavigationIntent intent)
            {
                if (windowId <= 0)
                    throw new ArgumentOutOfRangeException(nameof(windowId));
                if (requestId <= 0)
                    throw new ArgumentOutOfRangeException(nameof(requestId));
                PageToken = pageToken ?? throw new ArgumentNullException(nameof(pageToken));
                WindowId = windowId;
                RequestId = requestId;
                Intent = intent;
            }
        }

        private LogQuerySubmission() { }

        public static Fresh CreateFresh(int windowId, int requestId, LogFreshFilterSnapshot filter)
            => new(windowId, requestId, filter);

        public static Page CreatePage(int windowId, int requestId, string pageToken, LogNavigationIntent intent)
            => new(windowId, requestId, pageToken, intent);
    }

    public sealed record LogQueryFormatResult(bool Success, string? Packet, string? Error)
    {
        public static LogQueryFormatResult Ok(string packet) => new(true, packet, null);
        public static LogQueryFormatResult Fail(string error) => new(false, null, error);
    }

    public delegate bool LogQuerySender(LogQuerySubmission submission, out string error);
}
