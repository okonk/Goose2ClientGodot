using System;
using System.Globalization;
using Goose2Client.Logs;

namespace Goose2Client.Network.Packets
{
    public static class LogPacketParsing
    {
        public const int MaxSegmentCharacters = 12288;

        public static bool TrySplit(string packet, string prefix, int fieldCount, out string[] fields)
        {
            fields = null;
            if (packet == null || packet.Length < prefix.Length)
                return false;
            if (!packet.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            string[] parts = packet.Split(',');
            if (parts.Length != fieldCount)
                return false;
            fields = parts;
            return true;
        }

        private static bool TryParseWindowId(string firstField, string prefix, out int windowId)
        {
            windowId = 0;
            if (firstField.Length <= prefix.Length)
                return false;
            return TryParseInt32(firstField.Substring(prefix.Length), out windowId) && windowId > 0;
        }

        public static bool TryParseInt32(string text, out int value)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        public static bool TryParseInt64(string text, out long value)
            => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        public static bool IsValidSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment))
                return false;
            if (segment.Length > MaxSegmentCharacters)
                return false;
            bool paddingSeen = false;
            for (int i = 0; i < segment.Length; i++)
            {
                char c = segment[i];
                if (c == '=')
                {
                    paddingSeen = true;
                    continue;
                }
                if (paddingSeen)
                    return false;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/')
                    continue;
                return false;
            }
            return true;
        }

        public static LogTypeMetadata ParseLmt(string packet)
        {
            int wireLength = packet?.Length ?? 0;
            if (packet == null || !TrySplit(packet, "LMT", 4, out string[] f))
                return new LogTypeMetadata(false, 0, 0, wireLength, 0, "", "");
            if (!TryParseWindowId(f[0], "LMT", out int windowId))
                return new LogTypeMetadata(false, 0, 0, wireLength, 0, "", "");
            if (!TryParseInt32(f[1], out int typeId))
                return new LogTypeMetadata(false, windowId, 0, wireLength, 0, "", "");
            if (!ProtocolTextCodec.TryDecode(f[2], out string? group))
                return new LogTypeMetadata(false, windowId, 0, wireLength, typeId, "", "");
            if (!ProtocolTextCodec.TryDecode(f[3], out string? label))
                return new LogTypeMetadata(false, windowId, 0, wireLength, typeId, group, "");
            return new LogTypeMetadata(true, windowId, 0, wireLength, typeId, group, label);
        }

        public static LogMapMetadata ParseLmm(string packet)
        {
            int wireLength = packet?.Length ?? 0;
            if (packet == null || !TrySplit(packet, "LMM", 3, out string[] f))
                return new LogMapMetadata(false, 0, 0, wireLength, 0, "");
            if (!TryParseWindowId(f[0], "LMM", out int windowId))
                return new LogMapMetadata(false, 0, 0, wireLength, 0, "");
            if (!TryParseInt32(f[1], out int mapId))
                return new LogMapMetadata(false, windowId, 0, wireLength, 0, "");
            if (!ProtocolTextCodec.TryDecode(f[2], out string? mapName))
                return new LogMapMetadata(false, windowId, 0, wireLength, mapId, "");
            return new LogMapMetadata(true, windowId, 0, wireLength, mapId, mapName);
        }

        public static LogDefaultsMetadata ParseLmd(string packet)
        {
            int wireLength = packet?.Length ?? 0;
            if (packet == null || !TrySplit(packet, "LMD", 3, out string[] f))
                return new LogDefaultsMetadata(false, 0, 0, wireLength, 0, 0);
            if (!TryParseWindowId(f[0], "LMD", out int windowId))
                return new LogDefaultsMetadata(false, 0, 0, wireLength, 0, 0);
            if (!TryParseInt64(f[1], out long start) || !TryParseInt64(f[2], out long end))
                return new LogDefaultsMetadata(false, windowId, 0, wireLength, 0, 0);
            if (start >= end)
                return new LogDefaultsMetadata(false, windowId, 0, wireLength, 0, 0);
            return new LogDefaultsMetadata(true, windowId, 0, wireLength, start, end);
        }

        public static LogResultBegin ParseLrb(string packet)
        {
            int wireLength = packet?.Length ?? 0;
            if (packet == null || !TrySplit(packet, "LRB", 2, out string[] f))
                return new LogResultBegin(false, 0, 0, wireLength);
            int windowId = 0;
            if (TryParseWindowId(f[0], "LRB", out int parsedWindow))
                windowId = parsedWindow;
            int requestId = 0;
            if (TryParseInt32(f[1], out int parsedRequest) && parsedRequest > 0)
                requestId = parsedRequest;
            bool valid = windowId > 0 && requestId > 0;
            return new LogResultBegin(valid, windowId, requestId, wireLength);
        }

        public static LogResultData ParseLrd(string packet)
        {
            int wireLength = packet?.Length ?? 0;
            if (packet == null || !TrySplit(packet, "LRD", 6, out string[] f))
                return new LogResultData(false, 0, 0, wireLength, 0, 0, 0, "");
            int windowId = 0;
            if (TryParseWindowId(f[0], "LRD", out int parsedWindow))
                windowId = parsedWindow;
            int requestId = 0;
            if (TryParseInt32(f[1], out int parsedRequest) && parsedRequest > 0)
                requestId = parsedRequest;
            if (windowId == 0 || requestId == 0)
                return new LogResultData(false, windowId, requestId, wireLength, 0, 0, 0, "");
            if (!TryParseInt32(f[2], out int rowOrdinal) || rowOrdinal < 0)
                return new LogResultData(false, windowId, requestId, wireLength, 0, 0, 0, "");
            if (!TryParseInt32(f[3], out int chunkIndex) || chunkIndex < 0)
                return new LogResultData(false, windowId, requestId, wireLength, rowOrdinal, 0, 0, "");
            if (!TryParseInt32(f[4], out int chunkCount) || chunkCount <= 0)
                return new LogResultData(false, windowId, requestId, wireLength, rowOrdinal, chunkIndex, 0, "");
            if (!IsValidSegment(f[5]))
                return new LogResultData(false, windowId, requestId, wireLength, rowOrdinal, chunkIndex, chunkCount, "");
            return new LogResultData(true, windowId, requestId, wireLength, rowOrdinal, chunkIndex, chunkCount, f[5]);
        }

        public static LogResultFinish ParseLrf(string packet)
        {
            int wireLength = packet?.Length ?? 0;
            if (packet == null || !TrySplit(packet, "LRF", 5, out string[] f))
                return new LogResultFinish(false, 0, 0, wireLength, false, "", "");
            int windowId = 0;
            if (TryParseWindowId(f[0], "LRF", out int parsedWindow))
                windowId = parsedWindow;
            int requestId = 0;
            if (TryParseInt32(f[1], out int parsedRequest) && parsedRequest > 0)
                requestId = parsedRequest;
            if (windowId == 0 || requestId == 0)
                return new LogResultFinish(false, windowId, requestId, wireLength, false, "", "");
            if (f[2] != "0" && f[2] != "1")
                return new LogResultFinish(false, windowId, requestId, wireLength, false, "", "");
            bool hasMore = f[2] == "1";
            if (!LogPageTokenCodec.IsCanonical(f[3]))
                return new LogResultFinish(false, windowId, requestId, wireLength, hasMore, "", "");
            if (hasMore)
            {
                if (!LogPageTokenCodec.IsCanonical(f[4]))
                    return new LogResultFinish(false, windowId, requestId, wireLength, hasMore, f[3], "");
                return new LogResultFinish(true, windowId, requestId, wireLength, hasMore, f[3], f[4]);
            }
            if (f[4].Length != 0)
                return new LogResultFinish(false, windowId, requestId, wireLength, hasMore, f[3], "");
            return new LogResultFinish(true, windowId, requestId, wireLength, hasMore, f[3], "");
        }

        public static LogResultError ParseLrx(string packet)
        {
            int wireLength = packet?.Length ?? 0;
            if (packet == null || !TrySplit(packet, "LRX", 3, out string[] f))
                return new LogResultError(false, 0, 0, wireLength, "");
            int windowId = 0;
            if (TryParseWindowId(f[0], "LRX", out int parsedWindow))
                windowId = parsedWindow;
            int requestId = 0;
            if (TryParseInt32(f[1], out int parsedRequest) && parsedRequest > 0)
                requestId = parsedRequest;
            if (windowId == 0 || requestId == 0)
                return new LogResultError(false, windowId, requestId, wireLength, "");
            if (!ProtocolTextCodec.TryDecode(f[2], out string? message))
                return new LogResultError(false, windowId, requestId, wireLength, "");
            return new LogResultError(true, windowId, requestId, wireLength, message);
        }
    }
}
