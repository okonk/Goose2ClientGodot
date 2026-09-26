using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Goose2Client.Logs;

namespace Goose2Client.Network.Packets
{
    public static class LogQueryPacket
    {
        public const int MaxParticipantUtf8Bytes = 64;
        public const int MaxTextUtf8Bytes = 4096;
        public const int MaxPacketCharacters = 8192;

        public static LogQueryFormatResult Format(LogQuerySubmission.Fresh submission, int knownTypeCount)
        {
            if (submission == null)
                return LogQueryFormatResult.Fail("submission is missing");
            LogFreshFilterSnapshot filter = submission.Filter;
            if (filter == null)
                return LogQueryFormatResult.Fail("filter is missing");
            if (knownTypeCount < 0)
                return LogQueryFormatResult.Fail("known type count is invalid");
            if (filter.StartUnixMs >= filter.EndUnixMs)
                return LogQueryFormatResult.Fail("start must be before end");
            if (filter.MapId < 0)
                return LogQueryFormatResult.Fail("map id must be non-negative");
            if (!ProtocolTextCodec.TryEncode((filter.Participant ?? "").Trim(), MaxParticipantUtf8Bytes, out string? participantB64))
                return LogQueryFormatResult.Fail("participant exceeds limit");
            if (!ProtocolTextCodec.TryEncode(filter.Text ?? "", MaxTextUtf8Bytes, out string? textB64))
                return LogQueryFormatResult.Fail("text exceeds limit");
            var distinct = new HashSet<int>();
            foreach (int typeId in filter.TypeIds ?? Array.Empty<int>())
            {
                if (typeId < 0)
                    return LogQueryFormatResult.Fail("type id must be non-negative");
                distinct.Add(typeId);
            }
            if (distinct.Count > knownTypeCount)
                return LogQueryFormatResult.Fail("too many selected types");
            var sorted = new List<int>(distinct);
            sorted.Sort();
            var typeBuilder = new StringBuilder();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0)
                    typeBuilder.Append('|');
                typeBuilder.Append(sorted[i].ToString(CultureInfo.InvariantCulture));
            }
            string packet = string.Concat(
                "LQS",
                submission.WindowId.ToString(CultureInfo.InvariantCulture), ",",
                submission.RequestId.ToString(CultureInfo.InvariantCulture), ",F,",
                filter.StartUnixMs.ToString(CultureInfo.InvariantCulture), ",",
                filter.EndUnixMs.ToString(CultureInfo.InvariantCulture), ",",
                participantB64, ",",
                filter.MapId.ToString(CultureInfo.InvariantCulture), ",",
                typeBuilder.ToString(), ",",
                textB64);
            if (packet.Length > MaxPacketCharacters)
                return LogQueryFormatResult.Fail("packet exceeds limit");
            return LogQueryFormatResult.Ok(packet);
        }

        public static LogQueryFormatResult Format(LogQuerySubmission.Page submission)
        {
            if (submission == null)
                return LogQueryFormatResult.Fail("submission is missing");
            if (!LogPageTokenCodec.IsCanonical(submission.PageToken))
                return LogQueryFormatResult.Fail("page token is invalid");
            string packet = string.Concat(
                "LQS",
                submission.WindowId.ToString(CultureInfo.InvariantCulture), ",",
                submission.RequestId.ToString(CultureInfo.InvariantCulture), ",P,",
                submission.PageToken);
            if (packet.Length > MaxPacketCharacters)
                return LogQueryFormatResult.Fail("packet exceeds limit");
            return LogQueryFormatResult.Ok(packet);
        }
    }
}
