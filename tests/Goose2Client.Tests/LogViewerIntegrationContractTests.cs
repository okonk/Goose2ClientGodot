using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Goose2Client.Tests;

public class LogViewerIntegrationContractTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    private static int Count(string text, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    [Fact]
    public void GameHud_OwnsExactlyOneLogViewerWindowBeforeTheManagers()
    {
        string hud = Read("Scripts/UI/GameHud.cs");
        Assert.Contains("public LogViewerWindow LogViewer { get; private set; }", hud);
        Assert.Contains("LogViewer = Add<LogViewerWindow>(\"res://Scenes/UI/LogViewerWindow.tscn\");", hud);
        Assert.Equal(1, Count(hud, "Add<LogViewerWindow>"));
        Assert.Equal(3, Count(hud, "LogViewerWindow"));

        int window = hud.IndexOf("LogViewer = Add<LogViewerWindow>", StringComparison.Ordinal);
        int ready = hud.IndexOf("public override void _Ready()", StringComparison.Ordinal);
        int quest = hud.IndexOf("new QuestWindowManager()", StringComparison.Ordinal);
        int info = hud.IndexOf("new InfoWindowCreator()", StringComparison.Ordinal);
        int option = hud.IndexOf("new OptionListWindowManager()", StringComparison.Ordinal);
        Assert.True(ready >= 0 && window > ready, "LogViewerWindow must be instantiated in _Ready");
        Assert.True(window < quest, "LogViewerWindow must be created before QuestWindowManager");
        Assert.True(window < info, "LogViewerWindow must be created before InfoWindowCreator");
        Assert.True(window < option, "LogViewerWindow must be created before OptionListWindowManager");
    }

    [Fact]
    public void Window_Listeners_AreRegisteredAfterControlsAndRemovedSymmetrically()
    {
        string window = Read("Scripts/UI/LogViewerWindow.cs");
        string[] packetTypes =
        {
            "MakeWindowPacket", "EndWindowPacket", "CloseWindowPacket",
            "LogTypeMetadataPacket", "LogMapMetadataPacket", "LogDefaultsMetadataPacket",
            "LogResultBeginPacket", "LogResultDataPacket", "LogResultFinishPacket", "LogResultErrorPacket",
        };
        foreach (string type in packetTypes)
        {
            Assert.True(Count(window, $"Listen<{type}>") == 1, $"{type} Listen");
            Assert.True(Count(window, $"Remove<{type}>") == 1, $"{type} Remove");
        }
        Assert.Equal(1, Count(window, "Disconnected +="));
        Assert.Equal(1, Count(window, "Disconnected -="));
        Assert.Equal(1, Count(window, "SocketError +="));
        Assert.Equal(1, Count(window, "SocketError -="));
        Assert.Contains("_logic.OnTeardown()", window);

        int lastControl = window.LastIndexOf("_quickMap = GetNode", StringComparison.Ordinal);
        int firstListen = window.IndexOf("Listen<MakeWindowPacket>(", StringComparison.Ordinal);
        int scale = window.IndexOf("ScaleRegister();", StringComparison.Ordinal);
        Assert.True(lastControl >= 0 && firstListen > lastControl, "listeners must register after controls exist");
        Assert.True(firstListen < scale, "listeners must register inside _Ready");
    }

    [Fact]
    public void SearchPaths_UseOnlyTheInternalSender_AndProductionBindsTryLogQuery()
    {
        string window = Read("Scripts/UI/LogViewerWindow.cs");
        string client = Read("Scripts/Network/NetworkClient.cs");
        Assert.Contains("public bool TryLogQuery(LogQuerySubmission submission, out string error)", client);
        Assert.Contains("QuerySender = GameManager.Instance.NetworkClient.TryLogQuery;", window);
        Assert.Contains("_logic.Search();", window);
        Assert.Contains("_logic.Next();", window);
        Assert.Contains("_logic.Previous();", window);
        Assert.Equal(1, Count(window, "WindowButtonClick("));
        Assert.Contains("WindowButtonClick(WindowButtons.Close, id, 0)", window);
        Assert.True(!window.Contains("NetworkClient" + ".Send("), "window calls Send directly");
        Assert.DoesNotContain("TryLogQuery(", window);
        Assert.True(!window.Contains("new " + "Socket("), "window opens a socket");
    }

    [Fact]
    public void NoTestOrWindowPath_SubclassesNetworkClientOrDrivesSendForLqs()
    {
        string subclass = ":" + " NetworkClient";
        string openSocket = "new " + "Socket(";
        string directSend = "NetworkClient" + ".Send(";
        string window = Read("Scripts/UI/LogViewerWindow.cs");
        Assert.True(!window.Contains(subclass), "window subclasses NetworkClient");
        Assert.True(!window.Contains(openSocket), "window opens a socket");
        Assert.True(!window.Contains(directSend), "window calls Send directly");

        string testDirectory = Path.Combine(RepositoryRoot(), "tests", "Goose2Client.Tests");
        foreach (string file in Directory.GetFiles(testDirectory, "*.cs"))
        {
            string source = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            Assert.True(!source.Contains(subclass), name + " subclasses NetworkClient");
            Assert.True(!source.Contains(openSocket), name + " opens a socket");
            Assert.True(!source.Contains(directSend), name + " calls Send directly");
        }
    }
}
