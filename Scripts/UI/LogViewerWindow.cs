using System;
using Godot;
using Goose2Client;
using Goose2Client.Logs;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class LogViewerWindow : BaseWindow
{
    protected override bool DefaultVisible => false;
    protected override bool Resizable => true;
    protected override Vector2 MinResizeSize => LogViewerLayout.MinSize;

    private static readonly string[] PresetLabels =
    {
        "Last hour", "Previous 24 hours", "Previous 7 days", "Previous 30 days", "Custom UTC"
    };

    private static readonly string[] TypeGroups =
    {
        "Communication", "Sessions/Security", "Social", "Items/Economy", "GM Actions", "Other/Retired"
    };

    private readonly LogViewerWindowLogic _logic = new(DateTime.UtcNow);
    private LogViewerState _state => _logic.State;
    private bool _listenersRegistered;
    private OptionButton _preset;
    private LineEdit _customStart;
    private LineEdit _customEnd;
    private LineEdit _participant;
    private LineEdit _map;
    private LineEdit _text;
    private Button _typesButton;
    private Button _search;
    private Button _clear;
    private Label _status;
    private Label _applied;
    private Tree _tree;
    private TextEdit _details;
    private ItemList _suggestions;
    private Button _previous;
    private Button _next;
    private Button _copy;
    private Button _quickType;
    private Button _quickPrimary;
    private Button _quickRelated;
    private Button _quickMap;
    private PopupMenu _typesPopup = new();

    public LogViewerState State => _logic.State;

    public LogQuerySender? QuerySender
    {
        get => _logic.QuerySender;
        set => _logic.QuerySender = value;
    }

    public override void _Ready()
    {
        base._Ready();

        Visible = false;

        _preset = GetNode<OptionButton>("Content/PresetRow/PresetOptionButton");
        _customStart = GetNode<LineEdit>("Content/PresetRow/CustomStartField");
        _customEnd = GetNode<LineEdit>("Content/PresetRow/CustomEndField");
        _participant = GetNode<LineEdit>("Content/ParticipantRow/ParticipantField");
        _typesButton = GetNode<Button>("Content/ParticipantRow/TypesButton");
        _map = GetNode<LineEdit>("Content/MapRow/MapField");
        _text = GetNode<LineEdit>("Content/MapRow/TextField");
        _suggestions = GetNode<ItemList>("Content/MapSuggestions");
        _search = GetNode<Button>("Content/ActionRow/SearchButton");
        _clear = GetNode<Button>("Content/ActionRow/ClearButton");
        _status = GetNode<Label>("Content/ActionRow/StatusLabel");
        _applied = GetNode<Label>("Content/ActionRow/AppliedFilterLabel");
        _tree = GetNode<Tree>("Content/Split/ResultsPanel/ResultsTree");
        _details = GetNode<TextEdit>("Content/Split/DetailsPanel/DetailsText");
        _previous = GetNode<Button>("Content/Split/DetailsPanel/DetailsActions/PreviousButton");
        _next = GetNode<Button>("Content/Split/DetailsPanel/DetailsActions/NextButton");
        _copy = GetNode<Button>("Content/Split/DetailsPanel/DetailsActions/CopyButton");
        _quickType = GetNode<Button>("Content/Split/DetailsPanel/QuickActions/QuickTypeButton");
        _quickPrimary = GetNode<Button>("Content/Split/DetailsPanel/QuickActions/QuickPrimaryButton");
        _quickRelated = GetNode<Button>("Content/Split/DetailsPanel/QuickActions/QuickRelatedButton");
        _quickMap = GetNode<Button>("Content/Split/DetailsPanel/QuickActions/QuickMapButton");

        for (int i = 0; i < PresetLabels.Length; i++)
            _preset.AddItem(PresetLabels[i]);

        _tree.Columns = 6;
        for (int i = 0; i < LogViewerLayout.ColumnHeaders.Length; i++)
        {
            _tree.SetColumnTitle(i, LogViewerLayout.ColumnHeaders[i]);
            _tree.SetColumnCustomMinimumWidth(i, (int)LogViewerLayout.ColumnMinimums[i]);
            _tree.SetColumnExpandRatio(i, (int)Math.Round(LogViewerLayout.ColumnExpandRatios[i] * 10f));
        }

        _typesPopup.Name = "TypesPopup";
        _typesPopup.IdPressed += id => OnTypeItemPressed((int)id);
        AddChild(_typesPopup);

        _preset.ItemSelected += i => OnPresetSelected((int)i);
        _customStart.TextChanged += v => { ClearExactDefaults(); _state.Draft.StartText = v; RenderStatus(); };
        _customEnd.TextChanged += v => { ClearExactDefaults(); _state.Draft.EndText = v; RenderStatus(); };
        _participant.TextChanged += v => { _state.Draft.Participant = v; RenderStatus(); };
        _map.TextChanged += v => { _state.Draft.MapText = v; RenderSuggestions(); RenderStatus(); };
        _text.TextChanged += v => { _state.Draft.Text = v; RenderStatus(); };
        _customStart.TextSubmitted += _ => OnSearch();
        _customEnd.TextSubmitted += _ => OnSearch();
        _participant.TextSubmitted += _ => OnSearch();
        _map.TextSubmitted += _ => OnSearch();
        _text.TextSubmitted += _ => OnSearch();
        _search.Pressed += OnSearch;
        _clear.Pressed += OnClear;
        _typesButton.Pressed += OpenTypesPopup;
        _suggestions.ItemSelected += i => OnSuggestionSelected((int)i);
        _tree.ItemSelected += OnTreeItemSelected;
        _previous.Pressed += OnPreviousPressed;
        _next.Pressed += OnNextPressed;
        _copy.Pressed += OnCopyPressed;
        _quickType.Pressed += () => ApplyQuick(LogQuickActionTarget.Type);
        _quickPrimary.Pressed += () => ApplyQuick(LogQuickActionTarget.Primary);
        _quickRelated.Pressed += () => ApplyQuick(LogQuickActionTarget.Related);
        _quickMap.Pressed += OnQuickMapPressed;

        var network = GameManager.Instance.NetworkClient;
        network.Disconnected += OnDisconnected;
        network.SocketError += OnSocketError;
        var packets = GameManager.Instance.PacketManager;
        packets.Listen<MakeWindowPacket>(OnMakeWindow);
        packets.Listen<EndWindowPacket>(OnEndWindow);
        packets.Listen<CloseWindowPacket>(OnCloseWindow);
        packets.Listen<LogTypeMetadataPacket>(OnLmt);
        packets.Listen<LogMapMetadataPacket>(OnLmm);
        packets.Listen<LogDefaultsMetadataPacket>(OnLmd);
        packets.Listen<LogResultBeginPacket>(OnLrb);
        packets.Listen<LogResultDataPacket>(OnLrd);
        packets.Listen<LogResultFinishPacket>(OnLrf);
        packets.Listen<LogResultErrorPacket>(OnLrx);
        _listenersRegistered = true;

        QuerySender = GameManager.Instance.NetworkClient.TryLogQuery;
        _logic.BindCloseSender(id => GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Close, id, 0));

        LogFilterValidator.ApplyPreset(_state.Draft, DateTime.UtcNow);
        SyncControlsFromDraft();
        RenderAll();

        ScaleRegister();
    }

    protected override void OnClosePressed()
    {
        _logic.Close();
        SyncControlsFromDraft();
        Visible = _logic.IsVisible;
        RenderAll();
        _logic.SendClose();
        base.OnClosePressed();
    }

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        var network = GameManager.Instance.NetworkClient;
        network.Disconnected -= OnDisconnected;
        network.SocketError -= OnSocketError;
        var packets = GameManager.Instance.PacketManager;
        packets.Remove<MakeWindowPacket>(OnMakeWindow);
        packets.Remove<EndWindowPacket>(OnEndWindow);
        packets.Remove<CloseWindowPacket>(OnCloseWindow);
        packets.Remove<LogTypeMetadataPacket>(OnLmt);
        packets.Remove<LogMapMetadataPacket>(OnLmm);
        packets.Remove<LogDefaultsMetadataPacket>(OnLmd);
        packets.Remove<LogResultBeginPacket>(OnLrb);
        packets.Remove<LogResultDataPacket>(OnLrd);
        packets.Remove<LogResultFinishPacket>(OnLrf);
        packets.Remove<LogResultErrorPacket>(OnLrx);
        _listenersRegistered = false;
        _logic.OnTeardown();
    }

    private void OnMakeWindow(object o)
    {
        if (_logic.OnMakeWindow((MakeWindowPacket)o))
        {
            SyncControlsFromDraft();
            Visible = _logic.IsVisible;
            RenderAll();
        }
    }

    private void OnEndWindow(object o)
    {
        if (_logic.OnEndWindow((EndWindowPacket)o))
        {
            Visible = _logic.IsVisible;
            RenderAll();
        }
    }

    private void OnCloseWindow(object o)
    {
        if (_logic.OnCloseWindow((CloseWindowPacket)o))
        {
            SyncControlsFromDraft();
            Visible = _logic.IsVisible;
            RenderAll();
        }
    }

    private void OnDisconnected()
    {
        _logic.OnDisconnected();
        SyncControlsFromDraft();
        Visible = _logic.IsVisible;
        RenderAll();
    }

    private void OnSocketError(Exception error)
    {
        _logic.OnSocketError();
        SyncControlsFromDraft();
        Visible = _logic.IsVisible;
        RenderAll();
    }

    private void OnLmt(object o)
    {
        if (_logic.FeedLmt((LogTypeMetadata)o))
            RenderAll();
    }

    private void OnLmm(object o)
    {
        if (_logic.FeedLmm((LogMapMetadata)o))
            RenderAll();
    }

    private void OnLmd(object o)
    {
        if (_logic.FeedLmd((LogDefaultsMetadata)o))
        {
            _customStart.Text = _state.Draft.StartText;
            _customEnd.Text = _state.Draft.EndText;
            RenderAll();
        }
    }

    private void OnLrb(object o)
    {
        _logic.FeedLrb((LogResultBegin)o);
        RenderAll();
    }

    private void OnLrd(object o)
    {
        _logic.FeedLrd((LogResultData)o);
        RenderAll();
    }

    private void OnLrf(object o)
    {
        _logic.FeedLrf((LogResultFinish)o);
        RenderAll();
    }

    private void OnLrx(object o)
    {
        _logic.FeedLrx((LogResultError)o);
        RenderAll();
    }

    private void OnPresetSelected(int index)
    {
        if (index < 0 || index >= PresetLabels.Length)
            return;
        var draft = _state.Draft;
        draft.Preset = (LogFilterPreset)index;
        if (draft.Preset != LogFilterPreset.Custom)
            LogFilterValidator.ApplyPreset(draft, DateTime.UtcNow);
        else
        {
            draft.DefaultStartUnixMs = null;
            draft.DefaultEndUnixMs = null;
        }
        _customStart.Editable = draft.Preset == LogFilterPreset.Custom;
        _customEnd.Editable = draft.Preset == LogFilterPreset.Custom;
        RenderStatus();
    }

    private void OnClear()
    {
        LogFilterValidator.Clear(_state.Draft, DateTime.UtcNow);
        SyncControlsFromDraft();
        RenderAll();
    }

    private void OnSearch()
    {
        _logic.Search();
        RenderAll();
    }

    private void OnPreviousPressed()
    {
        _logic.Previous();
        RenderAll();
    }

    private void OnNextPressed()
    {
        _logic.Next();
        RenderAll();
    }

    private void SyncControlsFromDraft()
    {
        var draft = _state.Draft;
        _preset.Selected = (int)draft.Preset;
        bool custom = draft.Preset == LogFilterPreset.Custom;
        _customStart.Editable = custom;
        _customEnd.Editable = custom;
        _customStart.Text = draft.StartText;
        _customEnd.Text = draft.EndText;
        _participant.Text = draft.Participant;
        _map.Text = draft.MapText;
        _text.Text = draft.Text;
        RenderSuggestions();
    }

    private void ClearExactDefaults()
    {
        _state.Draft.DefaultStartUnixMs = null;
        _state.Draft.DefaultEndUnixMs = null;
    }

    private void OpenTypesPopup()
    {
        _typesPopup.Clear();
        int nextId = 0;
        var types = _state.Metadata.Types;
        foreach (string group in TypeGroups)
        {
            bool first = true;
            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];
                if (type.Group != group)
                    continue;
                if (first)
                {
                    _typesPopup.AddItem(group, nextId);
                    _typesPopup.SetItemDisabled(nextId, true);
                    nextId++;
                    first = false;
                }
                _typesPopup.AddItem(type.Label, nextId);
                _typesPopup.SetItemMetadata(nextId, Variant.From(type.TypeId));
                _typesPopup.SetItemAsCheckable(nextId, true);
                _typesPopup.SetItemChecked(nextId, _state.Draft.SelectedTypeIds.Contains(type.TypeId));
                nextId++;
            }
        }
        var mouse = GetGlobalMousePosition();
        _typesPopup.Position = new Vector2I((int)mouse.X, (int)mouse.Y);
        _typesPopup.Popup();
    }

    private void OnTypeItemPressed(int id)
    {
        Variant metadata = _typesPopup.GetItemMetadata(id);
        if (metadata.VariantType != Variant.Type.Int)
            return;
        int typeId = metadata.As<int>();
        bool isChecked = _typesPopup.IsItemChecked(id);
        _typesPopup.SetItemChecked(id, !isChecked);
        var selected = _state.Draft.SelectedTypeIds;
        if (isChecked)
            selected.Remove(typeId);
        else if (!selected.Contains(typeId))
            selected.Add(typeId);
        RenderStatus();
        _typesPopup.Show();
    }

    private void RenderSuggestions()
    {
        _suggestions.Clear();
        string prefix = _map.Text;
        var maps = _state.Metadata.Maps;
        for (int i = 0; i < maps.Count; i++)
        {
            string display = maps[i].MapName + " (#" + maps[i].MapId + ")";
            if (!display.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            _suggestions.AddItem(display);
            _suggestions.SetItemMetadata(_suggestions.ItemCount - 1, Variant.From(maps[i].MapId));
        }
    }

    private void OnSuggestionSelected(int index)
    {
        if (index < 0 || index >= _suggestions.ItemCount)
            return;
        Variant metadata = _suggestions.GetItemMetadata(index);
        if (metadata.VariantType != Variant.Type.Int)
            return;
        string mapText = "#" + metadata.As<int>();
        _state.Draft.MapText = mapText;
        _map.Text = mapText;
        RenderSuggestions();
        RenderStatus();
    }

    private void OnTreeItemSelected()
    {
        TreeItem? item = _tree.GetSelected();
        if (item == null)
            return;
        Variant metadata = item.GetMeta("row_index", new Variant());
        if (metadata.VariantType != Variant.Type.Int)
            return;
        _state.SelectRow(metadata.As<int>());
        RenderDetails();
    }

    private void OnCopyPressed()
    {
        LogRow? row = SelectedRow();
        if (row == null)
            return;
        DisplayServer.ClipboardSet(LogDetailsFormatter.FormatClipboard(row));
    }

    private void ApplyQuick(LogQuickActionTarget target)
    {
        LogRow? row = SelectedRow();
        if (row == null)
            return;
        _state.ApplyQuickAction(target, row);
        SyncControlsFromDraft();
        RenderStatus();
    }

    private void OnQuickMapPressed()
    {
        LogRow? row = SelectedRow();
        if (row?.Map == null || !MapQuickFilterAvailable(row))
            return;
        _state.Draft.MapText = "#" + row.Map.Id;
        _map.Text = _state.Draft.MapText;
        RenderSuggestions();
        RenderStatus();
    }

    private static bool MapQuickFilterAvailable(LogRow row)
        => row.Map != null && row.Map.CanQuickFilter && row.Map.Id > 0 && row.Map.Id <= int.MaxValue;

    private LogRow? SelectedRow()
    {
        int index = _state.SelectionIndex;
        return index >= 0 && index < _state.Rows.Count ? _state.Rows[index] : null;
    }

    private void RenderAll()
    {
        RenderStatus();
        RenderRows();
        RenderDetails();
    }

    private void RenderStatus()
    {
        _status.Text = _state.DirtyStatusText ?? _state.StatusText;
        _applied.Text = _state.AppliedFilterDescription;
        _search.Disabled = _state.IsActive || !_state.IsReady;
        _previous.Disabled = _state.HistoryIndex <= 0 || _state.IsActive;
        _next.Disabled = _state.NextToken == null || _state.IsActive;
    }

    private void RenderRows()
    {
        _tree.Clear();
        TreeItem root = _tree.CreateItem();
        var rows = _state.Rows;
        for (int i = 0; i < rows.Count; i++)
        {
            TreeItem item = _tree.CreateItem(root);
            string[] cells = LogDetailsFormatter.TableColumns(rows[i]);
            for (int c = 0; c < cells.Length; c++)
                item.SetText(c, cells[c]);
            item.SetMeta("row_index", Variant.From(i));
        }
        _tree.DeselectAll();
        if (_state.SelectionIndex >= 0 && _state.SelectionIndex < rows.Count)
            _tree.SetSelected(root.GetChild(_state.SelectionIndex), 0);
    }

    private void RenderDetails()
    {
        LogRow? row = SelectedRow();
        if (row == null)
        {
            _details.Text = "";
            _copy.Disabled = true;
            _quickType.Visible = false;
            _quickPrimary.Visible = false;
            _quickRelated.Visible = false;
            _quickMap.Visible = false;
            return;
        }
        _details.Text = LogDetailsFormatter.FormatDetails(row);
        _copy.Disabled = false;
        _quickType.Visible = LogViewerState.TypeQuickFilterAvailable(_state, row);
        _quickPrimary.Visible = LogViewerState.PrimaryQuickFilterAvailable(_state, row);
        _quickRelated.Visible = LogViewerState.RelatedQuickFilterAvailable(_state, row);
        _quickMap.Visible = MapQuickFilterAvailable(row);
    }
}

internal sealed class LogViewerWindowLogic
{
    private readonly LogViewerState _state;
    private Action<int>? _closeSent;
    private int _closeWindowId;

    public LogViewerState State => _state;
    public LogQuerySender? QuerySender { get; set; }
    public bool IsVisible { get; private set; }

    public LogViewerWindowLogic(DateTime utcNow) => _state = new LogViewerState(utcNow);

    public void BindCloseSender(Action<int> closeSent) => _closeSent = closeSent;

    public bool OnMakeWindow(MakeWindowPacket packet)
    {
        if (packet.WindowFrame != WindowFrames.LogViewer)
            return false;
        _state.OnWindowReplacement(packet.WindowId);
        LogFilterValidator.ApplyPreset(_state.Draft, DateTime.UtcNow);
        IsVisible = false;
        return true;
    }

    public bool OnEndWindow(EndWindowPacket packet)
    {
        if (packet.WindowId != _state.WindowId)
            return false;
        IsVisible = true;
        return true;
    }

    public bool OnCloseWindow(CloseWindowPacket packet)
    {
        if (packet.WindowId != _state.WindowId)
            return false;
        _state.OnClose();
        IsVisible = false;
        return true;
    }

    public void OnDisconnected()
    {
        _state.OnDisconnected();
        IsVisible = false;
    }

    public void OnSocketError()
    {
        _state.OnSocketError();
        IsVisible = false;
    }

    public void OnTeardown()
    {
        _state.OnTeardown();
        IsVisible = false;
    }

    public void Close()
    {
        _closeWindowId = _state.WindowId;
        _state.OnClose();
        IsVisible = false;
    }

    public void SendClose()
    {
        int windowId = _closeWindowId;
        _closeWindowId = 0;
        if (windowId == 0)
            return;
        _closeSent?.Invoke(windowId);
    }

    public bool FeedLmt(LogTypeMetadata packet) => _state.FeedLmt(packet);
    public bool FeedLmm(LogMapMetadata packet) => _state.FeedLmm(packet);
    public bool FeedLmd(LogDefaultsMetadata packet) => _state.FeedLmd(packet);
    public bool FeedLrb(LogResultBegin packet) => _state.FeedLrb(packet);
    public bool FeedLrd(LogResultData packet) => _state.FeedLrd(packet);
    public bool FeedLrf(LogResultFinish packet) => _state.FeedLrf(packet);
    public bool FeedLrx(LogResultError packet) => _state.FeedLrx(packet);

    public bool Search()
    {
        if (_state.Search() is not LogQuerySubmission submission)
            return false;
        SendQuery(submission);
        return true;
    }

    public bool Next()
    {
        if (_state.Next() is not LogQuerySubmission submission)
            return false;
        SendQuery(submission);
        return true;
    }

    public bool Previous()
    {
        if (_state.Previous() is not LogQuerySubmission submission)
            return false;
        SendQuery(submission);
        return true;
    }

    private void SendQuery(LogQuerySubmission submission)
    {
        if (QuerySender is not { } sender)
        {
            _state.CancelActiveRequest(null);
            return;
        }
        if (!sender(submission, out string error))
            _state.CancelActiveRequest(error);
    }
}
