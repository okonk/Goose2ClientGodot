using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Goose2Client.Logs;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Goose2Client.UI;

namespace Goose2Client;

internal static class LogViewerSelfTest
{
    private const int Window = 5;
    private const int ReplacedWindow = 6;
    private const string T1 = "AAECAwQFBgcICQoLDA0ODw";
    private const string T2 = "BBECAwQFBgcICQoLDA0ODw";
    private const string T3 = "CCECAwQFBgcICQoLDA0ODw";
    private const long DefaultStart = 1700000000123L;
    private const long DefaultEnd = 1700086400456L;

    public static async System.Threading.Tasks.Task Run(GameManager gm)
    {
        // In-product gate behind a project arg; production with no arg never reaches this.
        await gm.ToSignal(gm.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool failed = false;
        try
        {
            await SelfTestBody(gm);
            GD.Print("[log_viewer_selftest] PASS");
        }
        catch (System.Exception e)
        {
            failed = true;
            GD.PrintErr($"ERR_log_viewer_selftest: {e.Message}");
        }
        gm.GetTree().Quit(failed ? 1 : 0);
    }

    private sealed class Capture
    {
        public readonly List<LogQuerySubmission> Submissions = new();
        public readonly List<string> Packets = new();
        public bool Accept { get; set; } = true;
        public string Error { get; set; } = "";

        public LogQuerySender Delegate => (LogQuerySubmission submission, out string error) =>
        {
            Submissions.Add(submission);
            Packets.Add(Format(submission));
            if (!Accept)
            {
                error = Error;
                return false;
            }
            error = "";
            return true;
        };

        private static string Format(LogQuerySubmission submission)
        {
            LogQueryFormatResult result = submission is LogQuerySubmission.Fresh fresh
                ? LogQueryPacket.Format(fresh, int.MaxValue)
                : LogQueryPacket.Format((LogQuerySubmission.Page)submission);
            return result.Packet;
        }
    }

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static string RowJson(long rowId, long utc, long typeId, string eventLabel, string eventGroup,
        string otherIdKind, string primary, string related, string mapJson, string raw, string summary, string original)
    {
        return "{\"rowId\":" + rowId + ",\"utcMilliseconds\":" + utc + ",\"typeId\":" + typeId +
            ",\"typeIsInteger\":true,\"eventLabel\":\"" + eventLabel + "\",\"eventGroup\":\"" + eventGroup +
            "\",\"otherIdKind\":\"" + otherIdKind + "\",\"primary\":" + primary + ",\"related\":" + related +
            ",\"map\":" + mapJson + ",\"raw\":" + raw + ",\"summary\":\"" + summary +
            "\",\"originalText\":\"" + original + "\"}";
    }

    private static string Split(string b64, int index, int count)
    {
        int step = (b64.Length + count - 1) / count;
        int start = index * step;
        if (start >= b64.Length)
            return b64.Substring(b64.Length - 1);
        int end = index == count - 1 ? b64.Length : Math.Min(b64.Length, start + step);
        return b64.Substring(start, end - start);
    }

    private static async System.Threading.Tasks.Task SelfTestBody(GameManager gm)
    {
        var tree = gm.GetTree();
        var applier = UiScaleApplier.Instance;

        async System.Threading.Tasks.Task Frame() => await gm.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        void Assert(bool cond, string msg)
        {
            if (!cond)
                throw new System.InvalidOperationException(msg);
        }

        tree.Root.Size = new Vector2I(1280, 720);
        var canvas = (Vector2I)tree.Root.GetVisibleRect().Size;
        Assert(canvas == new Vector2I(1280, 720), $"headless canvas {canvas} != 1280x720");

        gm.LoadSettings("log-viewer-selftest");
        applier.Apply(1f, ApplyReason.Startup);
        gm.EnsureHud();
        await Frame();

        int viewerCount = 0;
        foreach (var child in gm.Hud.GetChildren())
            if (child is LogViewerWindow) viewerCount++;
        Assert(viewerCount == 1, $"exactly one log viewer expected, found {viewerCount}");
        var viewer = gm.Hud.LogViewer;
        Assert(viewer != null && !viewer.Visible, "log viewer must exist and start hidden");
        Assert((int)WindowFrames.LogViewer == 29, $"log viewer frame {(int)WindowFrames.LogViewer} != 29");
        Assert(LogViewerLayout.DesignSize == new Vector2(1000, 620), $"design size {LogViewerLayout.DesignSize}");
        Assert(LogViewerLayout.MinSize == new Vector2(620, 340), $"min size {LogViewerLayout.MinSize}");
        Assert(viewer.Size == LogViewerLayout.DesignSize, $"log viewer size {viewer.Size} != {LogViewerLayout.DesignSize}");

        var preset = viewer.GetNode<OptionButton>("Content/PresetRow/PresetOptionButton");
        var customStart = viewer.GetNode<LineEdit>("Content/PresetRow/CustomStartField");
        var customEnd = viewer.GetNode<LineEdit>("Content/PresetRow/CustomEndField");
        var participant = viewer.GetNode<LineEdit>("Content/ParticipantRow/ParticipantField");
        var typesButton = viewer.GetNode<Button>("Content/ParticipantRow/TypesButton");
        var map = viewer.GetNode<LineEdit>("Content/MapRow/MapField");
        var text = viewer.GetNode<LineEdit>("Content/MapRow/TextField");
        var suggestions = viewer.GetNode<ItemList>("Content/MapSuggestions");
        var search = viewer.GetNode<Button>("Content/ActionRow/SearchButton");
        var clear = viewer.GetNode<Button>("Content/ActionRow/ClearButton");
        var status = viewer.GetNode<Label>("Content/ActionRow/StatusLabel");
        var applied = viewer.GetNode<Label>("Content/ActionRow/AppliedFilterLabel");
        var split = viewer.GetNode<HBoxContainer>("Content/Split");
        var resultsPanel = viewer.GetNode<VBoxContainer>("Content/Split/ResultsPanel");
        var detailsPanel = viewer.GetNode<VBoxContainer>("Content/Split/DetailsPanel");
        var resultsTree = viewer.GetNode<Tree>("Content/Split/ResultsPanel/ResultsTree");
        var detailsText = viewer.GetNode<TextEdit>("Content/Split/DetailsPanel/DetailsText");
        var previous = viewer.GetNode<Button>("Content/Split/DetailsPanel/DetailsActions/PreviousButton");
        var next = viewer.GetNode<Button>("Content/Split/DetailsPanel/DetailsActions/NextButton");
        var copy = viewer.GetNode<Button>("Content/Split/DetailsPanel/DetailsActions/CopyButton");
        var quickType = viewer.GetNode<Button>("Content/Split/DetailsPanel/QuickActions/QuickTypeButton");
        var quickPrimary = viewer.GetNode<Button>("Content/Split/DetailsPanel/QuickActions/QuickPrimaryButton");
        var quickRelated = viewer.GetNode<Button>("Content/Split/DetailsPanel/QuickActions/QuickRelatedButton");
        var quickMap = viewer.GetNode<Button>("Content/Split/DetailsPanel/QuickActions/QuickMapButton");
        var titleBar = viewer.GetNode<Control>("TitleBar");
        Assert(resultsTree.Columns == 6, $"tree columns {resultsTree.Columns} != 6");
        Assert(resultsTree.ColumnTitlesVisible, "tree column titles must be visible");
        for (int i = 0; i < LogViewerLayout.ColumnHeaders.Length; i++)
            Assert(resultsTree.GetColumnTitle(i) == LogViewerLayout.ColumnHeaders[i],
                $"tree column {i} title {resultsTree.GetColumnTitle(i)}");

        var cap = new Capture();
        viewer.QuerySender = cap.Delegate;
        var state = viewer.State;
        var chatLog = gm.Hud.Chat.GetNode<RichTextLabel>("Content/ChatLog");

        void Dispatch(string packet) => gm.HandlePacket(packet);

        // set_text does not emit text_changed in this Godot version; the handlers are idempotent.
        void Type(LineEdit field, string value)
        {
            field.Text = value;
            field.EmitSignal("text_changed", value);
        }

        int ActiveRequestId()
        {
            Assert(cap.Submissions.Count > 0, "no captured submission to read the request id from");
            var last = cap.Submissions[^1];
            return last is LogQuerySubmission.Fresh f ? f.RequestId : ((LogQuerySubmission.Page)last).RequestId;
        }

        Dispatch($"MKW{Window},29,Log Viewer,0,0,0,0,0,0,0,0");
        Assert(state.WindowId == Window, $"window id {state.WindowId} != {Window} after MKW");
        Assert(!state.IsReady, "metadata must not be complete before LMD");

        Dispatch($"LMT{Window},33,{B64("Other/Retired")},{B64("LevelUp")}");
        Dispatch($"LMT{Window},12,{B64("Communication")},{B64("Chat")}");
        Dispatch($"LMT{Window},7,{B64("Sessions/Security")},{B64("Login")}");
        Assert(state.Metadata.Types.Count == 3, $"types {state.Metadata.Types.Count} != 3");
        Assert(state.Metadata.Types[0].TypeId == 33 && state.Metadata.Types[0].Group == "Other/Retired"
            && state.Metadata.Types[0].Label == "LevelUp", "LMT order: first record must be 33 LevelUp");
        Assert(state.Metadata.Types[1].TypeId == 12 && state.Metadata.Types[1].Group == "Communication"
            && state.Metadata.Types[1].Label == "Chat", "LMT order: second record must be 12 Chat");
        Assert(state.Metadata.Types[2].TypeId == 7 && state.Metadata.Types[2].Group == "Sessions/Security"
            && state.Metadata.Types[2].Label == "Login", "LMT order: third record must be 7 Login");

        Dispatch($"LMM{Window},3,{B64("Home")}");
        Dispatch($"LMM{Window},44,{B64("A,B;Cé")}");
        Assert(state.Metadata.Maps.Count == 2, $"maps {state.Metadata.Maps.Count} != 2");
        Assert(state.Metadata.Maps[0].MapId == 3 && state.Metadata.Maps[0].MapName == "Home", "LMM 3 Home");
        Assert(state.Metadata.Maps[1].MapId == 44 && state.Metadata.Maps[1].MapName == "A,B;Cé",
            "LMM 44 must round-trip the delimiter/Unicode name");

        Assert(status.Text == "Waiting for log metadata…", $"status before LMD {status.Text}");
        Assert(search.Disabled, "search must be disabled before LMD");
        int subsBefore = cap.Submissions.Count;
        search.EmitSignal("pressed");
        await Frame();
        Assert(cap.Submissions.Count == subsBefore, "search before LMD must not submit");
        Assert(!state.IsActive, "search before LMD must not start a request");

        Dispatch($"LMD{Window},{DefaultStart},{DefaultEnd}");
        Assert(state.IsReady, "metadata must be complete after LMD");
        Assert(state.Metadata.DefaultStartUnixMs == DefaultStart && state.Metadata.DefaultEndUnixMs == DefaultEnd,
            "LMD defaults must be stored");
        Assert(state.Draft.DefaultStartUnixMs == DefaultStart && state.Draft.DefaultEndUnixMs == DefaultEnd,
            "draft must carry the exact LMD default milliseconds");
        Assert(state.Draft.StartUnixMs == DefaultStart && state.Draft.EndUnixMs == DefaultEnd,
            "draft committed range must be the exact LMD milliseconds");
        Assert(customStart.Text == "2023-11-14 22:13:20", $"custom start display {customStart.Text}");
        Assert(customEnd.Text == "2023-11-15 22:13:20", $"custom end display {customEnd.Text}");
        Assert(!search.Disabled, "search must be enabled after LMD");
        Assert(preset.Selected == 1, $"preset {preset.Selected} != Previous 24 hours");
        Assert(!customStart.Editable && !customEnd.Editable, "custom fields must be locked for presets");
        Assert(participant.Text == "" && map.Text == "" && text.Text == "", "filter fields must start empty");
        Assert(status.Text == "Ready", $"status after LMD {status.Text}");
        Assert(applied.Text == "", "applied filter must be empty before a search");
        Assert(suggestions.ItemCount == 0, "suggestions must be empty before the map field is touched");
        Type(map, "H");
        await Frame();
        string sug0 = suggestions.ItemCount > 0 ? suggestions.GetItemText(0) : "<none>";
        Assert(suggestions.ItemCount == 1 && sug0 == "Home (#3)",
            $"suggestion prefix must filter maps: count {suggestions.ItemCount} text0 {sug0}");
        Type(map, "");
        await Frame();
        Assert(suggestions.ItemCount == 2, $"suggestions {suggestions.ItemCount} != 2");
        Assert(suggestions.GetItemText(0) == "Home (#3)", "suggestion 0 text");
        Assert(suggestions.GetItemText(1) == "A,B;Cé (#44)", "suggestion 1 text");

        Dispatch($"ENW{Window}");
        Assert(viewer.Visible, "ENW must show the viewer");
        GD.Print("[log_viewer_selftest] OK metadata: MKW/LMT/LMM/LMD/ENW gating");

        var draftStart = state.Draft.StartUnixMs;
        var draftEnd = state.Draft.EndUnixMs;
        Assert(draftStart == DefaultStart && draftEnd == DefaultEnd, "draft range must be the exact LMD milliseconds");
        Type(participant, "#1234");
        Type(map, "#3");
        Type(text, "hello");
        state.Draft.SelectedTypeIds.Add(12);
        search.EmitSignal("pressed");
        await Frame();
        Assert(cap.Submissions.Count == subsBefore + 1, $"fresh search must capture exactly one submission ({cap.Submissions.Count - subsBefore})");
        var fresh = (LogQuerySubmission.Fresh)cap.Submissions[^1];
        Assert(fresh.WindowId == Window && fresh.RequestId == 1, "fresh submission window/request");
        Assert(fresh.Filter.StartUnixMs == DefaultStart && fresh.Filter.EndUnixMs == DefaultEnd,
            "fresh filter range must be the exact LMD milliseconds");
        Assert(fresh.Filter.Participant == "#1234" && fresh.Filter.MapId == 3 && fresh.Filter.Text == "hello",
            "fresh filter participant/map/text");
        Assert(fresh.Filter.TypeIds.Count == 1 && fresh.Filter.TypeIds[0] == 12, "fresh filter type ids");
        string freshWire = cap.Packets[^1];
        Assert(freshWire.Split(',').Length == 9, $"fresh LQS must have nine fields: {freshWire}");
        Assert(freshWire == $"LQS{Window},1,F,{DefaultStart},{DefaultEnd},{B64("#1234")},3,12,{B64("hello")}",
            $"fresh LQS wire {freshWire}");
        Assert(state.IsActive, "fresh search must be active");
        Assert(search.Disabled, "search must be disabled during an active request");
        Assert(status.Text == "Loading…", $"status while loading {status.Text}");

        const string Raw1 = "{\"playerId\":1001,\"playerIdIsInteger\":true,\"otherId\":2002,\"otherIdIsInteger\":true,\"mapId\":3,\"mapIdIsInteger\":true,\"mapX\":10,\"mapXIsInteger\":true,\"mapY\":20,\"mapYIsInteger\":true}";
        const string Raw2 = "{\"playerId\":2002,\"playerIdIsInteger\":true,\"otherId\":5001,\"otherIdIsInteger\":true,\"mapId\":0,\"mapIdIsInteger\":true,\"mapX\":0,\"mapXIsInteger\":true,\"mapY\":0,\"mapYIsInteger\":true}";
        string row1 = RowJson(101, 1700000100000L, 12, "Chat", "Communication", "Player",
            "{\"label\":\"Player\",\"kind\":\"Player\",\"id\":1001,\"name\":\"Alice\",\"canQuickFilter\":true}",
            "null", "{\"id\":3,\"name\":\"Home\",\"canQuickFilter\":true}", Raw1, "Alice said hello", "<Alice> hello");
        string row2 = RowJson(102, 1700000200000L, 33, "LevelUp", "Other/Retired", "Item",
            "{\"label\":\"Item\",\"kind\":\"Item\",\"id\":5001,\"name\":\"Sword\",\"canQuickFilter\":false}",
            "{\"label\":\"Player\",\"kind\":\"Player\",\"id\":2002,\"name\":\"Bob\",\"canQuickFilter\":true}",
            "null", Raw2, "Bob leveled up", "Bob reached level 5");
        string row3 = RowJson(103, 1700000300000L, 7, "Login", "Sessions/Security", "Player",
            "{\"label\":\"Player\",\"kind\":\"Player\",\"id\":3003,\"name\":\"Carol\",\"canQuickFilter\":true}",
            "null", "{\"id\":44,\"name\":\"A,B;Cé\",\"canQuickFilter\":true}", Raw1, "Carol logged in", "Carol connected");
        string row4 = RowJson(104, 1700000400000L, 12, "Chat", "Communication", "Unused",
            "{\"label\":\"Stored\",\"kind\":\"StoredValue\",\"id\":null,\"name\":\"Gold\",\"canQuickFilter\":false}",
            "null", "null", Raw2, "Gold changed", "Balance updated");

        string r1a = Split(B64(row1), 0, 3);
        string r1b = Split(B64(row1), 1, 3);
        string r1c = Split(B64(row1), 2, 3);
        string r2a = Split(B64(row2), 0, 2);
        string r2b = Split(B64(row2), 1, 2);
        Assert(r1a.Length > 0 && r1b.Length > 0 && r1c.Length > 0 && r2a.Length > 0 && r2b.Length > 0, "chunks must be nonempty");

        Dispatch($"LRB{Window},1");
        await Frame();
        Dispatch($"LRD{Window},1,0,0,3,{r1a}");
        await Frame();
        Dispatch("CUP9001,Selftest");
        Dispatch($"LRD{Window},1,0,1,3,{r1b}");
        await Frame();
        Dispatch("CUP9002,Selftest");
        Dispatch($"LRD{Window},1,0,2,3,{r1c}");
        Dispatch($"LRD{Window},1,1,0,2,{r2a}");
        await Frame();
        Dispatch("CUP9003,Selftest");
        Dispatch($"LRD{Window},1,1,1,2,{r2b}");
        await Frame();
        Assert(resultsTree.GetRoot().GetChildCount() == 0, "tree must stay empty until LRF");
        Assert(state.IsActive, "request must stay active before LRF");

        Dispatch($"LRF{Window},1,1,{T1},{T2}");
        await Frame();
        Assert(!state.IsActive, "fresh commit must clear the active request");
        Assert(state.Rows.Count == 2 && state.Rows[0].RowId == 101 && state.Rows[1].RowId == 102,
            "fresh commit must stage both rows");
        Assert(state.History.Count == 1 && state.History[0] == T1 && state.History[0].Length == 22,
            "first history token must be nonempty and canonical");
        Assert(LogPageTokenCodec.IsCanonical(state.History[0]), "first history token must be canonical");
        Assert(state.HistoryIndex == 0 && state.CurrentToken == T1 && state.NextToken == T2, "fresh paging state");
        Assert(status.Text == "Showing 2 rows (up to 50), page 1", $"status after commit {status.Text}");
        Assert(!next.Disabled && previous.Disabled, "paging buttons after fresh commit");
        var root = resultsTree.GetRoot();
        Assert(root.GetChildCount() == 2, $"tree rows {root.GetChildCount()} != 2");
        var cells0 = LogDetailsFormatter.TableColumns(state.Rows[0]);
        for (int c = 0; c < 6; c++)
            Assert(root.GetChild(0).GetText(c) == cells0[c], $"tree cell 0,{c} {root.GetChild(0).GetText(c)}");
        var cells1 = LogDetailsFormatter.TableColumns(state.Rows[1]);
        for (int c = 0; c < 6; c++)
            Assert(root.GetChild(1).GetText(c) == cells1[c], $"tree cell 1,{c} {root.GetChild(1).GetText(c)}");

        resultsTree.SetSelected(root.GetChild(0), 0);
        await Frame();
        Assert(detailsText.Text == LogDetailsFormatter.FormatDetails(state.Rows[0]), "details must match row 1");
        Assert(detailsText.Text.Split('\n').Length == 32, "details must carry all 32 keys");
        Assert(!copy.Disabled, "copy must be enabled with a selection");
        Assert(quickType.Visible && quickPrimary.Visible && !quickRelated.Visible && quickMap.Visible,
            "row 1 quick-action visibility");
        resultsTree.SetSelected(root.GetChild(1), 0);
        await Frame();
        Assert(detailsText.Text == LogDetailsFormatter.FormatDetails(state.Rows[1]), "details must match row 2");
        Assert(quickType.Visible && !quickPrimary.Visible && quickRelated.Visible && !quickMap.Visible,
            "row 2 quick-action visibility");
        GD.Print("[log_viewer_selftest] OK fresh search + chunked commit + details");

        int popupSubsBefore = cap.Submissions.Count;
        typesButton.EmitSignal("pressed");
        await Frame();
        var typesPopup = viewer.GetNode<PopupMenu>("TypesPopup");
        Assert(typesPopup.ItemCount == 6, $"types popup must list three headers and three entries, found {typesPopup.ItemCount}");
        int[] eventIndexes = { 1, 3, 5 };
        bool[] expectedChecked = { true, false, false };
        for (int i = 0; i < eventIndexes.Length; i++)
        {
            int idx = eventIndexes[i];
            Assert(typesPopup.IsItemCheckable(idx), $"type entry {idx} must be checkable");
            Assert(!typesPopup.IsItemDisabled(idx), $"type entry {idx} must be enabled");
            Assert(typesPopup.IsItemChecked(idx) == expectedChecked[i], $"type entry {idx} checked state must match the draft");
        }
        Assert(typesPopup.IsItemDisabled(0) && !typesPopup.IsItemCheckable(0), "group header must stay disabled and not checkable");
        typesPopup.EmitSignal("id_pressed", 3);
        await Frame();
        Assert(typesPopup.IsItemChecked(3), "toggled type entry must show checked");
        Assert(state.Draft.SelectedTypeIds.Contains(7) && state.Draft.SelectedTypeIds.Contains(12),
            "toggling a type entry must update the draft selection");
        Assert(cap.Submissions.Count == popupSubsBefore, "toggling a type entry must not publish a query");
        typesPopup.EmitSignal("id_pressed", 3);
        await Frame();
        Assert(!typesPopup.IsItemChecked(3), "second toggle must uncheck the entry");
        Assert(state.Draft.SelectedTypeIds.Count == 1 && state.Draft.SelectedTypeIds[0] == 12,
            "toggling back must restore the draft selection");
        Assert(!state.IsDirty, "toggling back must leave the draft clean against the applied filter");
        Assert(cap.Submissions.Count == popupSubsBefore, "toggling must never publish a query");
        GD.Print("[log_viewer_selftest] OK type popup entries are checkable and toggle without querying");

        next.EmitSignal("pressed");
        await Frame();
        Assert(cap.Submissions.Count == subsBefore + 2, "next must capture exactly one more submission");
        var page = (LogQuerySubmission.Page)cap.Submissions[^1];
        Assert(page.WindowId == Window && page.RequestId == 2 && page.PageToken == T2
            && page.Intent == LogNavigationIntent.Next, "next must page with the next token");
        string pageWire = cap.Packets[^1];
        Assert(pageWire.Split(',').Length == 4, $"page LQS must have four fields: {pageWire}");
        Assert(pageWire == $"LQS{Window},2,P,{T2}", $"page LQS wire {pageWire}");

        string p3a = Split(B64(row3), 0, 2);
        string p3b = Split(B64(row3), 1, 2);
        string p4a = Split(B64(row4), 0, 2);
        string p4b = Split(B64(row4), 1, 2);
        Dispatch($"LRB{Window},2");
        Dispatch($"LRD{Window},2,0,0,2,{p3a}");
        await Frame();
        Dispatch("CUP9004,Selftest");
        Dispatch($"LRD{Window},2,0,1,2,{p3b}");
        Dispatch($"LRD{Window},2,1,0,2,{p4a}");
        await Frame();
        Dispatch($"LRD{Window},2,1,1,2,{p4b}");
        Dispatch($"LRF{Window},2,1,{T2},{T3}");
        await Frame();
        Assert(state.Rows.Count == 2 && state.Rows[0].RowId == 103 && state.Rows[1].RowId == 104,
            "page 2 must commit its rows");
        Assert(state.History.Count == 2 && state.History[0] == T1 && state.History[1] == T2, "history after next");
        Assert(state.HistoryIndex == 1 && state.CurrentToken == T2 && state.NextToken == T3, "page 2 paging state");
        Assert(status.Text == "Showing 2 rows (up to 50), page 2", $"status page 2 {status.Text}");
        Assert(!next.Disabled && !previous.Disabled, "paging buttons on page 2");
        GD.Print("[log_viewer_selftest] OK next page with matching current token");

        previous.EmitSignal("pressed");
        await Frame();
        var prev = (LogQuerySubmission.Page)cap.Submissions[^1];
        Assert(prev.RequestId == 3 && prev.PageToken == T1 && prev.Intent == LogNavigationIntent.Previous,
            "previous must use the stored first token");
        Assert(cap.Packets[^1] == $"LQS{Window},3,P,{T1}", $"previous LQS wire {cap.Packets[^1]}");
        Dispatch($"LRB{Window},3");
        Dispatch($"LRD{Window},3,0,0,1,{B64(row1)}");
        await Frame();
        Dispatch($"LRD{Window},3,1,0,1,{B64(row2)}");
        Dispatch($"LRF{Window},3,1,{T1},{T2}");
        await Frame();
        Assert(state.HistoryIndex == 0 && state.CurrentToken == T1 && state.NextToken == T2, "page 1 paging state");
        Assert(state.Rows[0].RowId == 101 && state.Rows[1].RowId == 102, "previous must restore page 1 rows");
        Assert(status.Text == "Showing 2 rows (up to 50), page 1", $"status page 1 {status.Text}");
        GD.Print("[log_viewer_selftest] OK previous page with stored token");

        void ExpectProtocolFailure(string name)
        {
            Assert(!state.IsActive, $"{name}: request must be inactive");
            Assert(state.StatusText == "Protocol failure.", $"{name}: status {state.StatusText}");
            Assert(status.Text == "Protocol failure.", $"{name}: label {status.Text}");
            Assert(state.Rows.Count == 2 && state.Rows[0].RowId == 101, $"{name}: committed rows must be untouched");
        }

        search.EmitSignal("pressed");
        await Frame();
        int req = ActiveRequestId();
        Dispatch($"LRB{Window},{req}");
        Dispatch($"LRD{Window},{req},0,0,2,{r1a}");
        Dispatch($"LRD{Window},{req},0,0,2,{r1a}");
        Dispatch($"LRF{Window},{req},0,{T1},");
        await Frame();
        ExpectProtocolFailure("duplicate chunk");

        search.EmitSignal("pressed");
        await Frame();
        req = ActiveRequestId();
        Dispatch($"LRB{Window},{req}");
        Dispatch($"LRD{Window},{req},0,1,2,{r1b}");
        Dispatch($"LRF{Window},{req},0,{T1},");
        await Frame();
        ExpectProtocolFailure("out-of-order chunk");

        search.EmitSignal("pressed");
        await Frame();
        req = ActiveRequestId();
        Dispatch($"LRB{Window},{req}");
        Dispatch($"LRD{Window},{req},0,0,2,{r1a}");
        Dispatch($"LRF{Window},{req},0,{T1},");
        await Frame();
        ExpectProtocolFailure("missing chunk");

        search.EmitSignal("pressed");
        await Frame();
        req = ActiveRequestId();
        Dispatch($"LRB{Window},{req}");
        Dispatch($"LRD{Window},{req},0,0,1,{new string('A', 12289)}");
        Dispatch($"LRF{Window},{req},0,{T1},");
        await Frame();
        ExpectProtocolFailure("oversize chunk");

        search.EmitSignal("pressed");
        await Frame();
        req = ActiveRequestId();
        Dispatch($"LRB{Window},{req}");
        Dispatch($"LRD{Window},{req},0,0,1,!!!");
        Dispatch($"LRF{Window},{req},0,{T1},");
        await Frame();
        ExpectProtocolFailure("malformed segment");

        search.EmitSignal("pressed");
        await Frame();
        req = ActiveRequestId();
        Dispatch($"LRB{Window},{req}");
        Dispatch($"LRD{Window},{req},0,0,1,{B64("not-json")}");
        Dispatch($"LRF{Window},{req},0,{T1},");
        await Frame();
        ExpectProtocolFailure("malformed JSON");

        search.EmitSignal("pressed");
        await Frame();
        req = ActiveRequestId();
        Dispatch($"LRB{Window},{req}");
        Dispatch($"LRD{Window},{req},0,0,1,{B64(row1)}");
        Dispatch($"LRF{Window},{req},2,{T1},{T2}");
        await Frame();
        ExpectProtocolFailure("malformed LRF");
        GD.Print("[log_viewer_selftest] OK malformed response paths reject without commit");

        search.EmitSignal("pressed");
        await Frame();
        req = ActiveRequestId();
        Dispatch($"LRB{Window},{req}");
        Dispatch($"LRD{Window},{req},0,0,1,{B64(row1)}");
        Dispatch($"LRD{Window},{req},1,0,1,{B64(row2)}");
        Dispatch($"LRF{Window},{req},1,{T1},{T2}");
        await Frame();
        Assert(state.Rows.Count == 2 && state.Rows[0].RowId == 101, "recovery search must commit cleanly");
        Assert(status.Text == "Showing 2 rows (up to 50), page 1", "recovery search must leave no stale buffers");

        string chatBefore = chatLog.Text;
        search.EmitSignal("pressed");
        await Frame();
        req = ActiveRequestId();
        Dispatch($"LRX{Window},{req},{B64("Query failed: bad range")}");
        await Frame();
        Assert(!state.IsActive, "LRX must clear the active request");
        Assert(state.StatusText == "Query failed: bad range", $"LRX status {state.StatusText}");
        Assert(status.Text == "Query failed: bad range", "LRX must render the exact inline text");
        Assert(state.Rows.Count == 2, "LRX must not touch committed rows");
        Assert(chatLog.Text == chatBefore, "chat must not be mutated by LRX");
        GD.Print("[log_viewer_selftest] OK LRX inline error without chat mutation");

        cap.Accept = false;
        cap.Error = "safe error text";
        search.EmitSignal("pressed");
        await Frame();
        Assert(cap.Submissions.Count == subsBefore + 13, "rejected search must still be captured");
        Assert(!state.IsActive, "rejected search must roll back the active request");
        Assert(state.StatusText == "safe error text", $"rejected search status {state.StatusText}");
        Assert(status.Text == "safe error text", "rejected search must render the safe error inline");
        Assert(state.Rows.Count == 2 && state.Rows[0].RowId == 101, "rejected search must keep committed rows");
        Assert(state.History.Count == 1 && state.History[0] == T1 && state.HistoryIndex == 0, "rejected search must keep history");
        cap.Accept = true;
        cap.Error = "";
        GD.Print("[log_viewer_selftest] OK sender rejection rolls back with safe error");

        string appliedBefore = applied.Text;
        var appliedFilterBefore = state.AppliedFilter;
        Assert(appliedBefore.Length > 0 && appliedFilterBefore != null, "applied filter must exist after a commit");
        Type(participant, "#9999");
        await Frame();
        Assert(state.IsDirty, "draft edit after commit must mark the state dirty");
        Assert(status.Text == LogViewerState.DirtyNotice, $"dirty status {status.Text}");
        Assert(applied.Text == appliedBefore, "applied description must remain unchanged while dirty");
        Assert(ReferenceEquals(state.AppliedFilter, appliedFilterBefore), "applied filter must remain unchanged while dirty");
        GD.Print("[log_viewer_selftest] OK dirty draft keeps applied text unchanged");

        Type(map, "");
        await Frame();
        Assert(suggestions.ItemCount == 2, "empty prefix must list all maps before the scale round trip");

        viewer.Size = new Vector2(800, 500);
        await Frame();
        Assert(resultsTree.Columns == 6, "tree must keep six columns after resize");
        Assert(split.Size.X > 0 && resultsPanel.Size.X > 0 && detailsPanel.Size.X > 0, "split panels must keep positive size");
        Assert(resultsPanel.Size.X > detailsPanel.Size.X, "results panel must keep its 58/42 majority");
        Assert(resultsTree.Size.X > 0 && resultsTree.Size.Y > 0, "tree must stay usable after resize");
        Assert(suggestions.Size.Y > 0, "suggestions must stay usable after resize");
        Assert(search.Size.X > 0 && clear.Size.X > 0 && next.Size.X > 0 && previous.Size.X > 0 && copy.Size.X > 0,
            "action buttons must stay usable after resize");
        viewer.Size = LogViewerLayout.DesignSize;
        await Frame();

        applier.Apply(2f, ApplyReason.UserCommit);
        await Frame();
        Assert(viewer.Size == new Vector2(1280, 720), $"2x viewer size {viewer.Size} must clamp to the canvas");
        Assert(titleBar.OffsetBottom == 48, $"2x title bar {titleBar.OffsetBottom} != 48");
        Assert(resultsTree.Columns == 6, "tree must keep six columns at 2x");
        Assert(suggestions.ItemCount == 2, "suggestions must survive 2x");
        Assert(search.Size.X > 0 && next.Size.X > 0 && previous.Size.X > 0 && copy.Size.X > 0,
            "buttons must stay usable at 2x");
        applier.Apply(1f, ApplyReason.UserCommit);
        await Frame();
        Assert(viewer.Size == LogViewerLayout.DesignSize, $"1x viewer size {viewer.Size} must restore the design size");
        Assert(titleBar.OffsetBottom == 24, $"1x title bar {titleBar.OffsetBottom} != 24");
        GD.Print("[log_viewer_selftest] OK resize + 1x/2x/1x scale round trip");

        search.EmitSignal("pressed");
        await Frame();
        req = ActiveRequestId();
        Dispatch($"LRB{Window},{req}");
        Dispatch($"LRD{Window},{req},0,0,2,{r1a}");
        await Frame();
        Assert(state.IsActive, "partial response must keep the request active");

        Dispatch($"CLW{Window}");
        await Frame();
        Assert(!viewer.Visible, "CLW must hide the viewer");
        Assert(!state.IsActive, "CLW must reset the active request");
        Assert(state.Rows.Count == 0, "CLW must clear rows");
        Assert(state.WindowId == 0 && !state.IsReady, "CLW must reset metadata");
        Assert(status.Text == "Waiting for log metadata…", $"CLW status {status.Text}");

        Dispatch($"LRD{Window},{req},0,1,2,{r1b}");
        Dispatch($"LRF{Window},{req},0,{T1},");
        await Frame();
        Assert(state.Rows.Count == 0 && !state.IsActive, "late finish after CLW must not commit");

        Dispatch($"MKW{ReplacedWindow},29,Log Viewer,0,0,0,0,0,0,0,0");
        await Frame();
        Assert(state.WindowId == ReplacedWindow, "replacement MKW must rebind the window id");
        Assert(!state.IsReady && !state.IsActive && !viewer.Visible, "replacement MKW must reset and stay hidden");
        Assert(state.NextRequestId == 1, "replacement MKW must reset request ids");

        Dispatch($"LRF{Window},{req},0,{T1},");
        await Frame();
        Assert(state.Rows.Count == 0 && !state.IsActive, "stale window finish must not commit");
        GD.Print("[log_viewer_selftest] OK CLW + replacement MKW reset without late commit");

        Assert(cap.Submissions.Count == cap.Packets.Count && cap.Submissions.Count > 0, "capture count mismatch");
        foreach (string p in cap.Packets)
            Assert(p != null && p.StartsWith("LQS"), $"captured packet must be an LQS query: {p}");
        GD.Print($"[log_viewer_selftest] OK {cap.Submissions.Count} captured queries, all LQS");
    }
}
