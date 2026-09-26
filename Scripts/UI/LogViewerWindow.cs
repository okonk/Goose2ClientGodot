using System;
using Godot;
using Goose2Client.Logs;

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

    private readonly LogViewerState _state = new(DateTime.UtcNow);
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

    public LogViewerState State => _state;

    public LogQuerySender? QuerySender { get; set; }

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
        _details = GetNode<TextEdit>("Content/DetailsPanel/DetailsText");
        _previous = GetNode<Button>("Content/DetailsPanel/DetailsActions/PreviousButton");
        _next = GetNode<Button>("Content/DetailsPanel/DetailsActions/NextButton");
        _copy = GetNode<Button>("Content/DetailsPanel/DetailsActions/CopyButton");
        _quickType = GetNode<Button>("Content/DetailsPanel/QuickActions/QuickTypeButton");
        _quickPrimary = GetNode<Button>("Content/DetailsPanel/QuickActions/QuickPrimaryButton");
        _quickRelated = GetNode<Button>("Content/DetailsPanel/QuickActions/QuickRelatedButton");
        _quickMap = GetNode<Button>("Content/DetailsPanel/QuickActions/QuickMapButton");

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
        _customStart.TextChanged += v => { _state.Draft.StartText = v; RenderStatus(); };
        _customEnd.TextChanged += v => { _state.Draft.EndText = v; RenderStatus(); };
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

        LogFilterValidator.ApplyPreset(_state.Draft, DateTime.UtcNow);
        SyncControlsFromDraft();
        RenderAll();

        ScaleRegister();
    }

    protected override void OnClosePressed()
    {
        _state.OnClose();
        base.OnClosePressed();
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
        if (_state.Search() is LogQuerySubmission submission)
            SendQuery(submission);
        RenderAll();
    }

    private void OnPreviousPressed()
    {
        if (_state.Previous() is LogQuerySubmission submission)
            SendQuery(submission);
        RenderAll();
    }

    private void OnNextPressed()
    {
        if (_state.Next() is LogQuerySubmission submission)
            SendQuery(submission);
        RenderAll();
    }

    private void SendQuery(LogQuerySubmission submission)
    {
        if (QuerySender is not { } sender)
            return;
        if (!sender(submission, out string error))
            _state.CancelActiveRequest(error);
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
        _map.Text = "#" + metadata.As<int>();
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
        _search.Disabled = _state.IsActive;
        _previous.Disabled = _state.HistoryIndex <= 0;
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
