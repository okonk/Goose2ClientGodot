using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Goose2Client.Logs;
using Goose2Client.Network;
using Goose2Client.Network.Packets;

namespace Goose2Client.Logs
{
    public sealed class LogViewerState
    {
        public const string FreshnessNotice = "Recent entries may be delayed by up to ten minutes.";
        public const string DirtyNotice = "Filters edited — Search to apply.";
        private const string ProtocolError = "Protocol failure.";

        private enum ActiveKind
        {
            None,
            Fresh,
            Page
        }

        private readonly LogFilterDraft _draft = new();
        private LogResponseAssembler _assembler = new();
        private LogMetadataState _metadata;
        private readonly List<string> _history = new();
        private string? _errorMessage;
        private LogFreshFilterSnapshot? _appliedFilter;
        private LogFreshFilterSnapshot? _lastSnapshot;
        private IReadOnlyList<LogRow> _rows = Array.Empty<LogRow>();
        private int _selectionIndex = -1;
        private int _historyIndex = -1;
        private string? _currentToken;
        private string? _nextToken;
        private ActiveKind _activeKind = ActiveKind.None;
        private string? _activeToken;
        private LogNavigationIntent _activeIntent;
        private int _activeRequestId;
        private int _nextRequestId = 1;
        private bool _hasCommittedPage;

        public LogViewerState(DateTime utcNow)
        {
            _metadata = new LogMetadataState(0);
            LogFilterValidator.ApplyPreset(_draft, utcNow);
        }

        public int WindowId => _metadata.WindowId;
        public LogMetadataState Metadata => _metadata;
        public LogFilterDraft Draft => _draft;
        public LogFreshFilterSnapshot? AppliedFilter => _appliedFilter;
        public IReadOnlyList<LogRow> Rows => _rows;
        public int SelectionIndex => _selectionIndex;
        public IReadOnlyList<string> History => _history;
        public int HistoryIndex => _historyIndex;
        public string? CurrentToken => _currentToken;
        public string? NextToken => _nextToken;
        public bool IsActive => _activeKind != ActiveKind.None;
        public bool IsReady => _metadata.Complete;
        public bool IsDirty => _appliedFilter != null && _lastSnapshot != null && IsDirtyDrafts();
        public string? DirtyStatusText => IsDirty ? DirtyNotice : null;

        private bool IsDirtyDrafts()
        {
            LogFilterValidationResult validation = LogFilterValidator.Validate(_draft, _metadata);
            if (!validation.Success || validation.Snapshot == null)
                return true;
            LogFreshFilterSnapshot current = validation.Snapshot;
            LogFreshFilterSnapshot applied = _lastSnapshot!;
            return current.StartUnixMs != applied.StartUnixMs
                || current.EndUnixMs != applied.EndUnixMs
                || current.Participant != applied.Participant
                || current.MapId != applied.MapId
                || !TypesEqual(current.TypeIds, applied.TypeIds)
                || current.Text != applied.Text;
        }

        private static bool TypesEqual(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }
        public int NextRequestId
        {
            get => _nextRequestId;
            set => _nextRequestId = value;
        }

        public string StatusText
        {
            get
            {
                if (_errorMessage != null)
                    return _errorMessage;
                if (!IsReady)
                    return "Waiting for log metadata…";
                if (IsActive)
                    return "Loading…";
                if (_rows.Count == 0)
                    return _hasCommittedPage ? "No persisted logs matched." : "Ready";
                return "Showing " + _rows.Count + " rows (up to 50), page " + (_historyIndex + 1);
            }
        }

        public string AppliedFilterDescription
        {
            get
            {
                if (_appliedFilter == null)
                    return "";
                var sb = new StringBuilder();
                sb.Append(FormatUtc(_appliedFilter.StartUnixMs)).Append(" → ").Append(FormatUtc(_appliedFilter.EndUnixMs));
                if (_appliedFilter.Participant.Length > 0)
                    sb.Append("  Participant: ").Append(_appliedFilter.Participant);
                if (_appliedFilter.MapId != 0)
                    sb.Append("  Map: #").Append(_appliedFilter.MapId);
                if (_appliedFilter.TypeIds.Count > 0)
                {
                    sb.Append("  Types: ");
                    for (int i = 0; i < _appliedFilter.TypeIds.Count; i++)
                    {
                        if (i > 0)
                            sb.Append(", ");
                        sb.Append(_appliedFilter.TypeIds[i].ToString(CultureInfo.InvariantCulture));
                    }
                }
                if (_appliedFilter.Text.Length > 0)
                    sb.Append("  Text: ").Append(_appliedFilter.Text);
                return sb.ToString();
            }
        }

        private static string FormatUtc(long unixMs)
            => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        public void OnWindowReplacement(int windowId)
        {
            ResetSession();
            _metadata = new LogMetadataState(windowId);
            _nextRequestId = 1;
        }

        public void OnClose() => ResetSession();
        public void OnDisconnected() => ResetSession();
        public void OnSocketError() => ResetSession();
        public void OnTeardown() => ResetSession();

        private void ResetSession()
        {
            _assembler = new LogResponseAssembler();
            _metadata = new LogMetadataState(0);
            _draft.Preset = LogFilterPreset.Previous24Hours;
            _draft.StartText = "";
            _draft.EndText = "";
            _draft.Participant = "";
            _draft.MapText = "";
            _draft.SelectedTypeIds = new List<int>();
            _draft.Text = "";
            _rows = Array.Empty<LogRow>();
            _selectionIndex = -1;
            _hasCommittedPage = false;
            _history.Clear();
            _historyIndex = -1;
            _currentToken = null;
            _nextToken = null;
            _appliedFilter = null;
            _lastSnapshot = null;
            _errorMessage = null;
            _activeKind = ActiveKind.None;
            _activeToken = null;
            _activeRequestId = 0;
        }

        public bool FeedLmt(LogTypeMetadata packet) => _metadata.FeedLmt(packet);
        public bool FeedLmm(LogMapMetadata packet) => _metadata.FeedLmm(packet);
        public bool FeedLmd(LogDefaultsMetadata packet) => _metadata.FeedLmd(packet);

        public object? Search()
        {
            if (!IsReady || IsActive)
                return null;
            LogFilterValidationResult validation = LogFilterValidator.Validate(_draft, _metadata);
            if (!validation.Success || validation.Snapshot == null)
            {
                _errorMessage = validation.Error;
                return null;
            }
            _lastSnapshot = validation.Snapshot;
            _assembler = new LogResponseAssembler();
            _activeKind = ActiveKind.Fresh;
            _activeToken = null;
            _activeRequestId = _nextRequestId;
            _nextRequestId = _nextRequestId == int.MaxValue ? 1 : _nextRequestId + 1;
            _errorMessage = null;
            return LogQuerySubmission.CreateFresh(_metadata.WindowId, _activeRequestId, validation.Snapshot);
        }

        public object? Next()
        {
            if (IsActive || _history.Count == 0 || _nextToken == null)
                return null;
            return BeginPage(_nextToken, LogNavigationIntent.Next);
        }

        public object? Previous()
        {
            if (IsActive || _history.Count == 0 || _historyIndex <= 0)
                return null;
            return BeginPage(_history[_historyIndex - 1], LogNavigationIntent.Previous);
        }

        private LogQuerySubmission.Page BeginPage(string token, LogNavigationIntent intent)
        {
            _assembler = new LogResponseAssembler();
            _activeKind = ActiveKind.Page;
            _activeToken = token;
            _activeIntent = intent;
            _activeRequestId = _nextRequestId;
            _nextRequestId = _nextRequestId == int.MaxValue ? 1 : _nextRequestId + 1;
            _errorMessage = null;
            return LogQuerySubmission.CreatePage(_metadata.WindowId, _activeRequestId, token, intent);
        }

        public bool FeedLrb(LogResultBegin packet)
        {
            if (packet == null || !IsActive)
                return false;
            if (packet.WindowId != _metadata.WindowId || packet.RequestId != _activeRequestId)
            {
                if (_assembler.HasStage)
                    FailProtocol();
                return false;
            }
            if (!packet.IsValid)
                return FailProtocol();
            return _assembler.FeedLrb(packet) || FailProtocol();
        }

        public bool FeedLrd(LogResultData packet)
        {
            if (packet == null || !IsActive)
                return false;
            if (packet.WindowId != _metadata.WindowId || packet.RequestId != _activeRequestId)
            {
                if (_assembler.HasStage)
                    FailProtocol();
                return false;
            }
            if (!packet.IsValid || !_assembler.HasStage)
                return FailProtocol();
            return _assembler.FeedLrd(packet) || FailProtocol();
        }

        public bool FeedLrf(LogResultFinish packet)
        {
            if (packet == null || !IsActive)
                return false;
            if (packet.WindowId != _metadata.WindowId || packet.RequestId != _activeRequestId)
            {
                if (_assembler.HasStage)
                    FailProtocol();
                return false;
            }
            if (!packet.IsValid || !_assembler.HasStage)
                return FailProtocol();
            if (!_assembler.FeedLrf(packet))
                return FailProtocol();
            LogResponseResult? result = _assembler.Result;
            if (result == null)
                return FailProtocol();
            if (_activeKind == ActiveKind.Fresh)
            {
                if (!LogPageTokenCodec.IsCanonical(result.CurrentToken))
                    return FailProtocol();
                CommitFresh(result);
            }
            else
            {
                if (result.CurrentToken != _activeToken)
                    return FailProtocol();
                CommitPage(result);
            }
            return true;
        }

        public bool FeedLrx(LogResultError packet)
        {
            if (packet == null || !IsActive)
                return false;
            if (packet.WindowId != _metadata.WindowId || packet.RequestId != _activeRequestId)
            {
                if (_assembler.HasStage)
                    FailProtocol();
                return false;
            }
            if (!packet.IsValid)
                return FailProtocol();
            _errorMessage = packet.SafeMessage;
            _assembler.Abort();
            _activeKind = ActiveKind.None;
            _activeToken = null;
            _activeRequestId = 0;
            return true;
        }

        private bool FailProtocol()
        {
            _errorMessage = ProtocolError;
            _assembler.Abort();
            _activeKind = ActiveKind.None;
            _activeToken = null;
            _activeRequestId = 0;
            return false;
        }

        private void CommitFresh(LogResponseResult result)
        {
            _rows = result.Rows;
            _selectionIndex = -1;
            _hasCommittedPage = true;
            _history.Clear();
            _history.Add(result.CurrentToken);
            _historyIndex = 0;
            _currentToken = result.CurrentToken;
            _nextToken = result.HasMore ? result.NextToken : null;
            _appliedFilter = _lastSnapshot;
            _activeKind = ActiveKind.None;
            _activeToken = null;
            _activeRequestId = 0;
            _errorMessage = null;
        }

        private void CommitPage(LogResponseResult result)
        {
            _rows = result.Rows;
            _selectionIndex = -1;
            _hasCommittedPage = true;
            if (_activeIntent == LogNavigationIntent.Previous)
            {
                _historyIndex--;
            }
            else if (_historyIndex + 1 < _history.Count && _history[_historyIndex + 1] == result.CurrentToken)
            {
                _historyIndex++;
            }
            else
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                _history.Add(result.CurrentToken);
                _historyIndex++;
            }
            _currentToken = result.CurrentToken;
            _nextToken = result.HasMore ? result.NextToken : null;
            _activeKind = ActiveKind.None;
            _activeToken = null;
            _activeRequestId = 0;
            _errorMessage = null;
        }

        public void SelectRow(int index)
        {
            if (index < 0 || index >= _rows.Count)
                return;
            _selectionIndex = index;
        }

        public static bool TypeQuickFilterAvailable(LogViewerState state, LogRow? row)
        {
            if (state == null || row == null || !row.TypeIsInteger)
                return false;
            if (row.TypeId < int.MinValue || row.TypeId > int.MaxValue)
                return false;
            for (int i = 0; i < state.Metadata.Types.Count; i++)
            {
                if (state.Metadata.Types[i].TypeId == (int)row.TypeId)
                    return true;
            }
            return false;
        }

        public static bool PrimaryQuickFilterAvailable(LogViewerState state, LogRow? row)
            => EntityQuickFilterAvailable(row?.Primary);

        public static bool RelatedQuickFilterAvailable(LogViewerState state, LogRow? row)
            => EntityQuickFilterAvailable(row?.Related);

        private static bool EntityQuickFilterAvailable(LogRowEntity? entity)
        {
            if (entity == null || !entity.CanQuickFilter || entity.Id == null)
                return false;
            if (entity.Kind != LogEntityKind.Player && entity.Kind != LogEntityKind.Map)
                return false;
            long id = entity.Id.Value;
            return id > 0 && id <= int.MaxValue;
        }

        public static LogQuickActions GetQuickActions(LogViewerState state, LogRow? row)
        {
            if (row == null)
                return new LogQuickActions(false, 0, false, LogEntityKind.Player, 0, false, LogEntityKind.Player, 0);
            bool typeAvailable = TypeQuickFilterAvailable(state, row);
            bool primaryAvailable = EntityQuickFilterAvailable(row.Primary);
            bool relatedAvailable = EntityQuickFilterAvailable(row.Related);
            return new LogQuickActions(
                typeAvailable,
                typeAvailable ? (int)row.TypeId : 0,
                primaryAvailable,
                row.Primary?.Kind ?? LogEntityKind.Player,
                primaryAvailable ? (int)row.Primary.Id!.Value : 0,
                relatedAvailable,
                row.Related?.Kind ?? LogEntityKind.Player,
                relatedAvailable ? (int)row.Related.Id!.Value : 0);
        }

        public void ApplyQuickAction(LogQuickActionTarget target, LogRow row)
        {
            switch (target)
            {
                case LogQuickActionTarget.Type:
                    if (!TypeQuickFilterAvailable(this, row))
                        return;
                    if (!_draft.SelectedTypeIds.Contains((int)row.TypeId))
                        _draft.SelectedTypeIds.Add((int)row.TypeId);
                    break;
                case LogQuickActionTarget.Primary:
                    if (!EntityQuickFilterAvailable(row.Primary))
                        return;
                    ApplyEntityKind(row.Primary);
                    break;
                case LogQuickActionTarget.Related:
                    if (!EntityQuickFilterAvailable(row.Related))
                        return;
                    ApplyEntityKind(row.Related);
                    break;
            }
        }

        private void ApplyEntityKind(LogRowEntity entity)
        {
            int id = (int)entity.Id!.Value;
            switch (entity.Kind)
            {
                case LogEntityKind.Player:
                    _draft.Participant = "#" + id;
                    break;
                case LogEntityKind.Map:
                    _draft.MapText = "#" + id;
                    break;
            }
        }
    }
}
