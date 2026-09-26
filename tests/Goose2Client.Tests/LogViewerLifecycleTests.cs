using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Goose2Client;
using Goose2Client.Logs;
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests
{
    public class LogViewerLifecycleTests
    {
        private const int Window = 5;
        private const int ReplacedWindow = 6;
        private const string Token1 = "AAECAwQFBgcICQoLDA0ODw";
        private const string Token2 = "BBECAwQFBgcICQoLDA0ODw";
        private const string Token3 = "CCECAwQFBgcICQoLDA0ODw";
        private static readonly DateTime Clock = new(2026, 9, 26, 12, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Sender
        {
            public List<LogQuerySubmission> Accepted { get; } = new();
            public List<LogQuerySubmission> Rejected { get; } = new();
            public string Error { get; set; } = "query rejected";
            public bool Accept { get; set; } = true;

            public LogQuerySender Delegate => (LogQuerySubmission submission, out string error) =>
            {
                if (Accept)
                {
                    Accepted.Add(submission);
                    error = "";
                    return true;
                }
                Rejected.Add(submission);
                error = Error;
                return false;
            };
        }

        private static LogViewerWindowLogic Open(Sender? sender = null)
        {
            var logic = new LogViewerWindowLogic(Clock);
            logic.QuerySender = sender?.Delegate;
            Assert.True(logic.OnMakeWindow(new MakeWindowPacket { WindowId = Window, WindowFrame = WindowFrames.LogViewer }));
            Assert.True(logic.FeedLmt(LogPacketParsing.ParseLmt($"LMT{Window},12,Q29tbXVuaWNhdGlvbg==,Q2hhdA==")));
            Assert.True(logic.FeedLmm(LogPacketParsing.ParseLmm($"LMM{Window},3,SG9tZQ==")));
            Assert.True(logic.FeedLmd(LogPacketParsing.ParseLmd($"LMD{Window},111,222")));
            Assert.True(logic.State.IsReady);
            return logic;
        }

        private static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        private static string MinimalRow(long rowId)
        {
            return "{\"rowId\":" + rowId +
                ",\"utcMilliseconds\":1700000000000," +
                "\"typeId\":12,\"typeIsInteger\":true," +
                "\"eventLabel\":\"Chat\",\"eventGroup\":\"Communication\"," +
                "\"otherIdKind\":\"Player\"," +
                "\"primary\":{\"label\":\"Player\",\"kind\":\"Player\",\"id\":1,\"name\":\"A\",\"canQuickFilter\":true}," +
                "\"related\":null,\"map\":null," +
                "\"raw\":{\"playerId\":1,\"playerIdIsInteger\":true,\"otherId\":2,\"otherIdIsInteger\":true,\"mapId\":3,\"mapIdIsInteger\":true,\"mapX\":4,\"mapXIsInteger\":true,\"mapY\":5,\"mapYIsInteger\":true}," +
                "\"summary\":\"s\"," +
                "\"originalText\":\"t\"}";
        }

        private static void DeliverFreshPage(LogViewerWindowLogic logic, int request, string json, string token, string next = "", bool more = false)
        {
            Assert.True(logic.FeedLrb(LogPacketParsing.ParseLrb($"LRB{Window},{request}")));
            Assert.True(logic.FeedLrd(LogPacketParsing.ParseLrd($"LRD{Window},{request},0,0,1,{B64(json)}")));
            Assert.True(logic.FeedLrf(LogPacketParsing.ParseLrf($"LRF{Window},{request},{(more ? "1" : "0")},{token},{next}")));
        }

        [Fact]
        public void WindowFrames_LogViewerIs29AndExistingValuesAreUnchanged()
        {
            Assert.Equal(1, (int)WindowFrames.Toolbar);
            Assert.Equal(2, (int)WindowFrames.Inventory);
            Assert.Equal(3, (int)WindowFrames.Spellbook);
            Assert.Equal(4, (int)WindowFrames.Hotbar);
            Assert.Equal(5, (int)WindowFrames.Buffbar);
            Assert.Equal(6, (int)WindowFrames.FPS);
            Assert.Equal(7, (int)WindowFrames.HP);
            Assert.Equal(8, (int)WindowFrames.MP);
            Assert.Equal(9, (int)WindowFrames.SP);
            Assert.Equal(10, (int)WindowFrames.XP);
            Assert.Equal(11, (int)WindowFrames.Equipped);
            Assert.Equal(12, (int)WindowFrames.Chat);
            Assert.Equal(13, (int)WindowFrames.Vendor);
            Assert.Equal(14, (int)WindowFrames.Party);
            Assert.Equal(15, (int)WindowFrames.TwoSlot);
            Assert.Equal(16, (int)WindowFrames.FourSlot);
            Assert.Equal(17, (int)WindowFrames.SixSlot);
            Assert.Equal(18, (int)WindowFrames.EightSlot);
            Assert.Equal(19, (int)WindowFrames.TenSlot);
            Assert.Equal(20, (int)WindowFrames.Quest);
            Assert.Equal(21, (int)WindowFrames.Quest2);
            Assert.Equal(22, (int)WindowFrames.GenericInfo);
            Assert.Equal(23, (int)WindowFrames.DiscardButton);
            Assert.Equal(24, (int)WindowFrames.Paper);
            Assert.Equal(25, (int)WindowFrames.Trade);
            Assert.Equal(26, (int)WindowFrames.Bank);
            Assert.Equal(27, (int)WindowFrames.OptionList);
            Assert.Equal(28, (int)WindowFrames.Custom);
            Assert.Equal(29, (int)WindowFrames.LogViewer);
            Assert.Equal(29, (int)Enum.GetValues<WindowFrames>().Max());
        }

        [Fact]
        public void MkwFrame29_ResetsEverythingBeforeAssigningAndIgnoresOtherFrames()
        {
            var sender = new Sender();
            var logic = Open(sender);
            Assert.True(logic.Search());
            DeliverFreshPage(logic, 1, MinimalRow(1), Token1, Token2, more: true);
            logic.Next();
            Assert.True(logic.FeedLrb(LogPacketParsing.ParseLrb($"LRB{Window},2")));
            Assert.True(logic.FeedLrd(LogPacketParsing.ParseLrd($"LRD{Window},2,0,0,1,{B64(MinimalRow(2))}")));
            logic.OnEndWindow(new EndWindowPacket { WindowId = Window });
            Assert.True(logic.IsVisible);

            Assert.False(logic.OnMakeWindow(new MakeWindowPacket { WindowId = 77, WindowFrame = WindowFrames.Custom }));
            Assert.Single(logic.State.Rows);
            Assert.Equal(Window, logic.State.WindowId);
            Assert.True(logic.IsVisible);

            Assert.True(logic.OnMakeWindow(new MakeWindowPacket { WindowId = ReplacedWindow, WindowFrame = WindowFrames.LogViewer }));
            var s = logic.State;
            Assert.Equal(ReplacedWindow, s.WindowId);
            Assert.Empty(s.Rows);
            Assert.Empty(s.History);
            Assert.Equal(-1, s.HistoryIndex);
            Assert.Null(s.CurrentToken);
            Assert.Null(s.NextToken);
            Assert.Null(s.AppliedFilter);
            Assert.False(s.IsActive);
            Assert.False(s.IsReady);
            Assert.Equal(1, s.NextRequestId);
            Assert.Equal(LogFilterPreset.Previous24Hours, s.Draft.Preset);
            Assert.True(s.Draft.EndUnixMs > s.Draft.StartUnixMs);
            Assert.False(logic.IsVisible);

            Assert.False(logic.FeedLmt(LogPacketParsing.ParseLmt($"LMT{Window},12,Q29tbXVuaWNhdGlvbg==,Q2hhdA==")));
            Assert.False(logic.FeedLmm(LogPacketParsing.ParseLmm($"LMM{Window},3,SG9tZQ==")));
            Assert.False(logic.FeedLmd(LogPacketParsing.ParseLmd($"LMD{Window},111,222")));
            Assert.False(s.IsReady);
            Assert.Empty(s.Metadata.Types);
            Assert.Empty(s.Metadata.Maps);

            Assert.True(logic.FeedLmt(LogPacketParsing.ParseLmt($"LMT{ReplacedWindow},12,Q29tbXVuaWNhdGlvbg==,Q2hhdA==")));
            Assert.Single(s.Metadata.Types);
            Assert.False(s.IsReady);
            Assert.True(logic.FeedLmm(LogPacketParsing.ParseLmm($"LMM{ReplacedWindow},3,SG9tZQ==")));
            Assert.Single(s.Metadata.Maps);
            Assert.False(s.IsReady);
            Assert.True(logic.FeedLmd(LogPacketParsing.ParseLmd($"LMD{ReplacedWindow},111,222")));
            Assert.True(s.IsReady);

            Assert.False(logic.OnEndWindow(new EndWindowPacket { WindowId = Window }));
            Assert.False(logic.IsVisible);
            Assert.True(logic.OnEndWindow(new EndWindowPacket { WindowId = ReplacedWindow }));
            Assert.True(logic.IsVisible);

            Assert.True(logic.Search());
            var fresh = Assert.IsType<LogQuerySubmission.Fresh>(sender.Accepted[^1]);
            Assert.Equal(ReplacedWindow, fresh.WindowId);
            Assert.Equal(1, fresh.RequestId);
        }

        [Fact]
        public void ExplicitActions_InvokeTheInjectedSenderExactlyOnceWithExactSubmissions()
        {
            var sender = new Sender();
            var logic = Open(sender);

            Assert.True(logic.Search());
            Assert.Single(sender.Accepted);
            var fresh = Assert.IsType<LogQuerySubmission.Fresh>(sender.Accepted[0]);
            Assert.Equal(Window, fresh.WindowId);
            Assert.Equal(1, fresh.RequestId);
            Assert.Equal(logic.State.Draft.StartUnixMs, fresh.Filter.StartUnixMs);
            Assert.Equal(logic.State.Draft.EndUnixMs, fresh.Filter.EndUnixMs);
            Assert.Empty(fresh.Filter.TypeIds);
            Assert.True(logic.State.IsActive);

            Assert.False(logic.Search());
            Assert.False(logic.Next());
            Assert.False(logic.Previous());
            Assert.Single(sender.Accepted);

            DeliverFreshPage(logic, 1, MinimalRow(1), Token1, Token2, more: true);
            Assert.True(logic.Next());
            Assert.Equal(2, sender.Accepted.Count);
            var page = Assert.IsType<LogQuerySubmission.Page>(sender.Accepted[1]);
            Assert.Equal(Window, page.WindowId);
            Assert.Equal(2, page.RequestId);
            Assert.Equal(Token2, page.PageToken);
            Assert.Equal(LogNavigationIntent.Next, page.Intent);
            Assert.True(logic.State.IsActive);

            Assert.False(logic.Next());
            Assert.False(logic.Previous());
            Assert.Equal(2, sender.Accepted.Count);

            logic.FeedLrb(LogPacketParsing.ParseLrb($"LRB{Window},2"));
            logic.FeedLrd(LogPacketParsing.ParseLrd($"LRD{Window},2,0,0,1,{B64(MinimalRow(2))}"));
            logic.FeedLrf(LogPacketParsing.ParseLrf($"LRF{Window},2,1,{Token2},{Token3}"));
            Assert.True(logic.Previous());
            Assert.Equal(3, sender.Accepted.Count);
            var previous = Assert.IsType<LogQuerySubmission.Page>(sender.Accepted[2]);
            Assert.Equal(3, previous.RequestId);
            Assert.Equal(Token1, previous.PageToken);
            Assert.Equal(LogNavigationIntent.Previous, previous.Intent);

            Assert.False(logic.Previous());
            Assert.Equal(3, sender.Accepted.Count);
            Assert.Empty(sender.Rejected);
        }

        [Fact]
        public void CapturingSender_ReceiveExactSubmissionsWithoutConstructingANetworkClient()
        {
            var sender = new Sender();
            var logic = Open(sender);
            logic.State.Draft.Participant = "bob";
            logic.State.Draft.MapText = "#3";
            logic.State.Draft.Text = "hello";
            logic.State.Draft.SelectedTypeIds.Add(12);

            Assert.True(logic.Search());
            var fresh = Assert.IsType<LogQuerySubmission.Fresh>(sender.Accepted[0]);
            Assert.Equal(Window, fresh.WindowId);
            Assert.Equal(1, fresh.RequestId);
            Assert.Equal(logic.State.Draft.StartUnixMs, fresh.Filter.StartUnixMs);
            Assert.Equal(logic.State.Draft.EndUnixMs, fresh.Filter.EndUnixMs);
            Assert.Equal("bob", fresh.Filter.Participant);
            Assert.Equal(3, fresh.Filter.MapId);
            Assert.Equal(new[] { 12 }, fresh.Filter.TypeIds.ToArray());
            Assert.Equal("hello", fresh.Filter.Text);

            DeliverFreshPage(logic, 1, MinimalRow(1), Token1, Token2, more: true);
            Assert.True(logic.Next());
            var page = Assert.IsType<LogQuerySubmission.Page>(sender.Accepted[1]);
            Assert.Equal(Window, page.WindowId);
            Assert.Equal(2, page.RequestId);
            Assert.Equal(Token2, page.PageToken);
            Assert.Equal(LogNavigationIntent.Next, page.Intent);

            Assert.Equal(2, sender.Accepted.Count);
            Assert.Empty(sender.Rejected);
        }

        [Fact]
        public void SenderRejection_RollsBackActiveStateAndKeepsCommittedDataIntact()
        {
            var sender = new Sender();
            var logic = Open(sender);
            Assert.True(logic.Search());
            DeliverFreshPage(logic, 1, MinimalRow(99), Token1, Token2, more: true);
            Assert.Single(logic.State.Rows);
            Assert.Equal(new[] { Token1 }, logic.State.History.ToArray());
            Assert.Equal(Token2, logic.State.NextToken);

            sender.Accept = false;
            Assert.True(logic.Next());
            Assert.Single(sender.Rejected);
            Assert.False(logic.State.IsActive);
            Assert.Equal("query rejected", logic.State.StatusText);
            Assert.Single(logic.State.Rows);
            Assert.Equal(0, logic.State.HistoryIndex);
            Assert.Equal(new[] { Token1 }, logic.State.History.ToArray());
            Assert.Equal(Token2, logic.State.NextToken);

            sender.Accept = true;
            Assert.True(logic.Next());
            Assert.Equal(2, sender.Accepted.Count);

            var rejecting = new Sender { Accept = false };
            var fresh = Open(rejecting);
            Assert.True(fresh.Search());
            Assert.Single(rejecting.Rejected);
            Assert.False(fresh.State.IsActive);
            Assert.Equal("query rejected", fresh.State.StatusText);
            Assert.Empty(fresh.State.Rows);
            Assert.Empty(fresh.State.History);
        }

        [Fact]
        public void TryLogQuery_FormatsExactPacketAndInvokesSendOnlyOnSuccess()
        {
            var client = new NetworkClient();
            Exception? socketError = null;
            client.SocketError += e => socketError = e;

            var bad = LogQuerySubmission.CreateFresh(10, 7, new LogFreshFilterSnapshot(2000, 1000, "bob", 0, Array.Empty<int>(), "hello"));
            Assert.False(client.TryLogQuery(bad, out string error));
            Assert.Equal("start must be before end", error);
            Assert.Null(socketError);

            var fresh = LogQuerySubmission.CreateFresh(10, 7, new LogFreshFilterSnapshot(1000, 2000, " bob ", 3, new List<int> { 5, 2, 5 }, "hello"));
            Assert.Equal("LQS10,7,F,1000,2000,Ym9i,3,2|5,aGVsbG8=", LogQueryPacket.Format(fresh, int.MaxValue).Packet);
            // A never-connected client throws inside Send's guard and surfaces it via SocketError,
            // so the event proves Send ran without a real socket.
            Assert.True(client.TryLogQuery(fresh, out string okError));
            Assert.Equal("", okError);
            Assert.IsType<NullReferenceException>(socketError);

            var page = LogQuerySubmission.CreatePage(10, 7, Token1, LogNavigationIntent.Next);
            Assert.Equal("LQS10,7,P," + Token1, LogQueryPacket.Format(page).Packet);
            Assert.True(client.TryLogQuery(page, out _));

            var badToken = LogQuerySubmission.CreatePage(10, 8, "short", LogNavigationIntent.Next);
            Assert.False(client.TryLogQuery(badToken, out string pageError));
            Assert.False(string.IsNullOrEmpty(pageError));
        }

        [Fact]
        public void PacketRouting_KeepsMultiCallChunksInvisibleUntilLrf_AndMalformedAbortsTerminally()
        {
            var logic = Open(new Sender());
            Assert.True(logic.Search());

            string row0 = MinimalRow(1);
            string row0B64 = B64(row0);
            int mid = row0B64.Length / 2;
            string row1 = MinimalRow(2);
            Assert.True(logic.FeedLrb(LogPacketParsing.ParseLrb($"LRB{Window},1")));
            Assert.True(logic.FeedLrd(LogPacketParsing.ParseLrd($"LRD{Window},1,0,0,2,{row0B64.Substring(0, mid)}")));
            Assert.Empty(logic.State.Rows);
            Assert.True(logic.State.IsActive);
            Assert.True(logic.FeedLrd(LogPacketParsing.ParseLrd($"LRD{Window},1,0,1,2,{row0B64.Substring(mid)}")));
            Assert.Empty(logic.State.Rows);
            Assert.True(logic.FeedLrd(LogPacketParsing.ParseLrd($"LRD{Window},1,1,0,1,{B64(row1)}")));
            Assert.Empty(logic.State.Rows);
            Assert.Equal("Loading…", logic.State.StatusText);
            Assert.True(logic.FeedLrf(LogPacketParsing.ParseLrf($"LRF{Window},1,0,{Token1},")));
            Assert.Equal(2, logic.State.Rows.Count);
            Assert.Equal(1L, logic.State.Rows[0].RowId);
            Assert.Equal(2L, logic.State.Rows[1].RowId);
            Assert.False(logic.State.IsActive);
            Assert.Equal("Showing 2 rows (up to 50), page 1", logic.State.StatusText);

            var malformed = Open(new Sender());
            Assert.True(malformed.Search());
            Assert.True(malformed.FeedLrb(LogPacketParsing.ParseLrb($"LRB{Window},1")));
            Assert.False(malformed.FeedLrd(LogPacketParsing.ParseLrd($"LRD{Window},1,0,0,1,QU!D")));
            Assert.Equal("Protocol failure.", malformed.State.StatusText);
            Assert.False(malformed.State.IsActive);
            Assert.False(malformed.FeedLrf(LogPacketParsing.ParseLrf($"LRF{Window},1,0,{Token1},")));
            Assert.Empty(malformed.State.Rows);
            Assert.Empty(malformed.State.History);
        }

        [Fact]
        public void LocalClose_ResetsAndHidesBeforeExactlyOneWbc_AndTeardownsSendNothing()
        {
            var closes = new List<int>();
            var logic = Open(new Sender());
            Assert.True(logic.Search());
            DeliverFreshPage(logic, 1, MinimalRow(1), Token1);
            logic.OnEndWindow(new EndWindowPacket { WindowId = Window });
            Assert.True(logic.IsVisible);
            logic.BindCloseSender(id => closes.Add(id));

            logic.Close();
            Assert.Empty(closes);
            Assert.False(logic.IsVisible);
            Assert.Empty(logic.State.Rows);
            Assert.Empty(logic.State.History);
            Assert.False(logic.State.IsActive);
            logic.SendClose();
            Assert.Equal(new[] { Window }, closes);
            logic.SendClose();
            Assert.Equal(1, closes.Count);

            var neverOpened = new LogViewerWindowLogic(Clock);
            neverOpened.BindCloseSender(id => closes.Add(id));
            neverOpened.Close();
            neverOpened.SendClose();
            Assert.Equal(1, closes.Count);

            var clw = Open();
            clw.BindCloseSender(id => closes.Add(id));
            clw.OnEndWindow(new EndWindowPacket { WindowId = Window });
            Assert.True(clw.OnCloseWindow(new CloseWindowPacket { WindowId = Window }));
            Assert.False(clw.IsVisible);
            Assert.Empty(clw.State.Rows);
            Assert.Equal(1, closes.Count);
            Assert.False(clw.OnCloseWindow(new CloseWindowPacket { WindowId = 99 }));

            var disconnected = Open();
            disconnected.OnDisconnected();
            Assert.False(disconnected.IsVisible);
            Assert.Empty(disconnected.State.Rows);
            Assert.Equal(1, closes.Count);

            var errored = Open();
            errored.OnSocketError();
            Assert.False(errored.IsVisible);
            Assert.Equal(1, closes.Count);

            var replaced = Open();
            Assert.True(replaced.OnMakeWindow(new MakeWindowPacket { WindowId = ReplacedWindow, WindowFrame = WindowFrames.LogViewer }));
            Assert.Equal(1, closes.Count);

            Assert.Equal(2, (int)WindowButtons.Close);
            string clientSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "Scripts/Network/NetworkClient.cs"));
            Assert.Contains("Send($\"WBC{(int)button},{windowId},{npcId},{unknownId1},{unknownId2}\")", clientSource);
            string windowSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "Scripts/UI/LogViewerWindow.cs"));
            Assert.Contains("WindowButtonClick(WindowButtons.Close, id, 0)", windowSource);
        }

        [Fact]
        public void TeardownAfterPartialChunks_ClearsStagingAndLatePacketsAreStale()
        {
            Action<LogViewerWindowLogic>[] teardowns =
            {
                l => l.Close(),
                l => l.OnDisconnected(),
                l => l.OnSocketError(),
                l => l.OnTeardown(),
                l => Assert.True(l.OnMakeWindow(new MakeWindowPacket { WindowId = ReplacedWindow, WindowFrame = WindowFrames.LogViewer })),
            };
            foreach (Action<LogViewerWindowLogic> teardown in teardowns)
            {
                var logic = Open(new Sender());
                Assert.True(logic.Search());
                Assert.True(logic.FeedLrb(LogPacketParsing.ParseLrb($"LRB{Window},1")));
                Assert.True(logic.FeedLrd(LogPacketParsing.ParseLrd($"LRD{Window},1,0,0,2,{B64(MinimalRow(1).Substring(0, 32))}")));
                Assert.True(logic.State.IsActive);

                teardown(logic);
                Assert.False(logic.State.IsActive);
                Assert.Empty(logic.State.Rows);
                Assert.Empty(logic.State.History);
                Assert.False(logic.IsVisible);
                Assert.False(logic.FeedLrd(LogPacketParsing.ParseLrd($"LRD{Window},1,0,1,2,{B64(MinimalRow(1).Substring(32))}")));
                Assert.False(logic.FeedLrf(LogPacketParsing.ParseLrf($"LRF{Window},1,0,{Token1},")));
                Assert.Empty(logic.State.Rows);
                Assert.Empty(logic.State.History);
            }
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
        }
    }
}
