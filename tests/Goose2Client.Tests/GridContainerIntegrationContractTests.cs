using System;
using System.IO;
using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class GridContainerIntegrationContractTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    [Fact]
    public void GameHud_RegistersGridContainerWindowManager_AfterOptionListWindows()
    {
        string hud = Read("Scripts/UI/GameHud.cs");
        Assert.Contains("AddChild(new GridContainerWindowManager());", hud);

        int option = hud.IndexOf("new OptionListWindowManager()", StringComparison.Ordinal);
        int grid = hud.IndexOf("new GridContainerWindowManager()", StringComparison.Ordinal);
        Assert.True(option >= 0 && grid >= 0, "both managers must be constructed in GameHud._Ready");
        Assert.True(grid > option, "GridContainerWindowManager must be added after OptionListWindowManager");
    }

    [Fact]
    public void BaseMultipleWindowManager_ExposesTryGetWindow()
    {
        string baseMgr = Read("Scripts/UI/BaseMultipleWindowManager.cs");
        Assert.Contains("protected bool TryGetWindow(int windowId, out T window) => _windows.TryGetValue(windowId, out window!);", baseMgr);
    }

    [Fact]
    public void Manager_ListensAndRemovesSlotPackets_Symmetrically()
    {
        string mgr = Read("Scripts/UI/GridContainerWindowManager.cs");
        Assert.Contains("Listen<GenericWindowSlotPacket>(OnGenericWindowSlot)", mgr);
        Assert.Contains("Listen<ClearGenericWindowSlotPacket>(OnClearGenericWindowSlot)", mgr);
        Assert.Contains("Remove<GenericWindowSlotPacket>(OnGenericWindowSlot)", mgr);
        Assert.Contains("Remove<ClearGenericWindowSlotPacket>(OnClearGenericWindowSlot)", mgr);
        Assert.Contains("base._Ready();", mgr);
        Assert.Contains("base._ExitTree();", mgr);
        Assert.Contains("TryGetWindow(p.WindowId, out var w)", mgr);
    }

    [Fact]
    public void Window_OnMakeWindow_SanitizesFrameParams()
    {
        string win = Read("Scripts/UI/GridContainerWindow.cs");
        int mkw = win.IndexOf("public override void OnMakeWindow(MakeWindowPacket packet)", StringComparison.Ordinal);
        Assert.True(mkw >= 0, "OnMakeWindow must be overridden");
        Assert.Contains("GridContainerMetrics.Sanitize(packet.FrameParam1, packet.FrameParam2)", win);
    }

    [Fact]
    public void Window_RunsSlotsWithSkipMeta()
    {
        string win = Read("Scripts/UI/GridContainerWindow.cs");
        Assert.Contains("SetMeta(UiScaleLayout.SkipMeta, true)", win);
    }

    [Fact]
    public void Window_IsNotADialog_AndHasADefaultPosition()
    {
        Assert.False(DefaultWindowLayout.IsDialog("GridContainer"));
        Assert.Equal(new Vector2(740, 360), DefaultWindowLayout.For("GridContainer"));
    }
}
