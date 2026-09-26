using System;
using System.Collections.Generic;
using Goose2Client.Logs;
using Goose2Client.Network.Packets;

namespace Goose2Client.Logs
{
    public sealed class LogResponseAssembler
    {
        public const int MaxRows = 50;
        public const int MaxDecodedRowBytes = 262144;
        public const int MaxStagedBytes = 4194304;

        public bool HasStage { get; private set; }
        public bool Aborted { get; private set; }
        public LogResponseResult? Result { get; private set; }

        private int _windowId;
        private int _requestId;
        private long _stagedBytes;
        private int _expectedOrdinal;
        private int _currentChunkCount;
        private bool _rowInProgress;
        private int _expectedIndex;
        private string _currentSegment = "";
        private readonly List<LogRow> _rows = new();

        public bool FeedLrb(LogResultBegin packet)
        {
            if (Aborted || packet == null)
                return false;
            if (HasStage)
            {
                Abort();
                return false;
            }
            if (!packet.IsValid || packet.WireLength + 1 > MaxStagedBytes)
                return false;
            _windowId = packet.WindowId;
            _requestId = packet.RequestId;
            _stagedBytes = packet.WireLength + 1;
            _expectedOrdinal = 0;
            _rows.Clear();
            HasStage = true;
            return true;
        }

        public bool FeedLrd(LogResultData packet)
        {
            if (Aborted || packet == null || !HasStage)
                return false;
            if (packet.WindowId != _windowId || packet.RequestId != _requestId)
            {
                Abort();
                return false;
            }
            if (!packet.IsValid || _stagedBytes + packet.WireLength + 1 > MaxStagedBytes)
            {
                Abort();
                return false;
            }
            if (packet.RowOrdinal != _expectedOrdinal)
            {
                Abort();
                return false;
            }
            if (_rowInProgress)
            {
                if (packet.ChunkIndex != _expectedIndex || packet.ChunkCount != _currentChunkCount)
                {
                    Abort();
                    return false;
                }
            }
            else
            {
                if (packet.ChunkIndex != 0 || packet.ChunkCount <= 0)
                {
                    Abort();
                    return false;
                }
                _currentChunkCount = packet.ChunkCount;
                _expectedIndex = 0;
                _currentSegment = "";
                _rowInProgress = true;
            }
            _stagedBytes += packet.WireLength + 1;
            _currentSegment += packet.Segment;
            _expectedIndex++;
            if (_expectedIndex == _currentChunkCount)
            {
                _rowInProgress = false;
                string aggregate = _currentSegment;
                _currentSegment = "";
                if (!TryCompleteRow(aggregate))
                {
                    Abort();
                    return false;
                }
                _expectedOrdinal++;
                if (_rows.Count > MaxRows)
                {
                    Abort();
                    return false;
                }
            }
            return true;
        }

        public bool FeedLrf(LogResultFinish packet)
        {
            if (Aborted || packet == null || !HasStage)
                return false;
            if (packet.WindowId != _windowId || packet.RequestId != _requestId)
            {
                Abort();
                return false;
            }
            if (!packet.IsValid || _stagedBytes + packet.WireLength + 1 > MaxStagedBytes)
            {
                Abort();
                return false;
            }
            if (_rowInProgress || _rows.Count > MaxRows)
            {
                Abort();
                return false;
            }
            _stagedBytes += packet.WireLength + 1;
            LogResponseResult result = new(_rows.ToArray(), packet.HasMore, packet.CurrentPageToken, packet.NextPageToken);
            ClearStage();
            Result = result;
            return true;
        }

        public bool FeedLrx(LogResultError packet)
        {
            if (Aborted || packet == null || !packet.IsValid)
                return false;
            if (HasStage && (packet.WindowId != _windowId || packet.RequestId != _requestId))
                return false;
            Abort();
            return true;
        }

        public bool Abort()
        {
            ClearStage();
            Result = null;
            Aborted = true;
            return true;
        }

        public bool OnClose() => Abort();
        public bool OnWindowReplacement() => Abort();
        public bool Dispose() => Abort();

        private bool TryCompleteRow(string aggregate)
        {
            if (aggregate.Length == 0 || aggregate.Length % 4 != 0)
                return false;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(aggregate);
            }
            catch (FormatException)
            {
                return false;
            }
            // .NET accepts noncanonical padding-bit spellings; the wire format is the canonical encoding of the bytes.
            if (Convert.ToBase64String(bytes) != aggregate)
                return false;
            if (bytes.Length == 0 || bytes.Length > MaxDecodedRowBytes)
                return false;
            if (!LogRowJsonParser.TryParse(bytes, out LogRow? row))
                return false;
            _rows.Add(row);
            return true;
        }

        private void ClearStage()
        {
            HasStage = false;
            _rowInProgress = false;
            _expectedOrdinal = 0;
            _currentChunkCount = 0;
            _expectedIndex = 0;
            _currentSegment = "";
            _rows.Clear();
            _stagedBytes = 0;
        }
    }
}
