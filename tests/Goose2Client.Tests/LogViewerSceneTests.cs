using System;
using System.IO;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class LogViewerSceneTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.godot")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find project.godot");
    }

    private static string Scene() => File.ReadAllText(Path.Combine(RepositoryRoot(), "Scenes/UI/LogViewerWindow.tscn"));

    [Fact]
    public void Root_UsesStandardChromeAndDesignSize()
    {
        var s = Scene();
        Assert.Contains("[node name=\"LogViewerWindow\" type=\"Control\"]", s);
        Assert.Contains("WindowName = \"LogViewer\"", s);
        Assert.Contains("offset_right = 1000.0", s);
        Assert.Contains("offset_bottom = 620.0", s);
        Assert.Contains("node name=\"Background\" type=\"TextureRect\"", s);
        Assert.Contains("anchors_preset = 15", s);
        Assert.Contains("node name=\"TitleBar\" type=\"Control\"", s);
        Assert.Contains("node name=\"TitleLabel\" type=\"Label\"", s);
        Assert.Contains("node name=\"CloseButton\" type=\"Button\"", s);
        Assert.Contains("node name=\"Content\" type=\"Control\"", s);
    }

    [Fact]
    public void FilterControls_ArePresent()
    {
        var s = Scene();
        Assert.Contains("node name=\"FreshnessLabel\" type=\"Label\"", s);
        Assert.Contains("text = \"Recent entries may be delayed by up to ten minutes.\"", s);
        Assert.Contains("node name=\"PresetOptionButton\" type=\"OptionButton\"", s);
        Assert.Contains("node name=\"CustomStartField\" type=\"LineEdit\"", s);
        Assert.Contains("node name=\"CustomEndField\" type=\"LineEdit\"", s);
        Assert.Contains("node name=\"ParticipantField\" type=\"LineEdit\"", s);
        Assert.Contains("node name=\"TypesButton\" type=\"Button\"", s);
        Assert.Contains("node name=\"MapField\" type=\"LineEdit\"", s);
        Assert.Contains("node name=\"MapSuggestions\" type=\"ItemList\"", s);
        Assert.Contains("node name=\"TextField\" type=\"LineEdit\"", s);
        Assert.Contains("node name=\"SearchButton\" type=\"Button\"", s);
        Assert.Contains("node name=\"ClearButton\" type=\"Button\"", s);
    }

    [Fact]
    public void StatusAndAppliedFilter_AreSeparateLabels()
    {
        var s = Scene();
        Assert.Contains("node name=\"StatusLabel\" type=\"Label\"", s);
        Assert.Contains("node name=\"AppliedFilterLabel\" type=\"Label\"", s);
    }

    [Fact]
    public void ResultsTree_IsUnsortedSixColumns()
    {
        var s = Scene();
        Assert.Contains("node name=\"ResultsTree\" type=\"Tree\"", s);
        Assert.Contains("columns = 6", s);
        Assert.Contains("columns_unsorted = true", s);
        Assert.DoesNotContain("columns_unsorted = false", s);
    }

    [Fact]
    public void Details_IsReadOnlyPlainTextWithExactActions()
    {
        var s = Scene();
        Assert.Contains("node name=\"DetailsText\" type=\"TextEdit\"", s);
        Assert.Contains("editable = false", s);
        Assert.Contains("node name=\"CopyButton\" type=\"Button\"", s);
        Assert.Contains("text = \"Copy\"", s);
        Assert.Contains("node name=\"PreviousButton\" type=\"Button\"", s);
        Assert.Contains("text = \"Previous\"", s);
        Assert.Contains("node name=\"NextButton\" type=\"Button\"", s);
        Assert.Contains("text = \"Next\"", s);
        Assert.Contains("node name=\"QuickTypeButton\" type=\"Button\"", s);
        Assert.Contains("node name=\"QuickPrimaryButton\" type=\"Button\"", s);
        Assert.Contains("node name=\"QuickRelatedButton\" type=\"Button\"", s);
        Assert.Contains("node name=\"QuickMapButton\" type=\"Button\"", s);
    }

    [Fact]
    public void Containers_ConsumeGrowthAndStayReachableAtMinimum()
    {
        var s = Scene();
        Assert.Contains("node name=\"ResultsPanel\" type=\"VBoxContainer\"", s);
        Assert.Contains("node name=\"DetailsPanel\" type=\"VBoxContainer\"", s);
        Assert.Contains("size_flags_stretch_ratio = 58", s);
        Assert.Contains("size_flags_stretch_ratio = 42", s);
        Assert.Contains("size_flags_vertical = 3", s);
        float contentHeight = LogViewerLayout.MinSize.Y - LogViewerLayout.TitleBarHeight;
        Assert.True(LogViewerLayout.FilterAreaHeight + LogViewerLayout.Margin < contentHeight);
    }

    [Fact]
    public void DeferredFeatures_AreAbsent()
    {
        var s = Scene();
        Assert.DoesNotContain("SortButton", s);
        Assert.DoesNotContain("TotalLabel", s);
        Assert.DoesNotContain("TotalCount", s);
        Assert.DoesNotContain("RefreshTimer", s);
        Assert.DoesNotContain("Timer", s);
        Assert.DoesNotContain("ExportButton", s);
        Assert.DoesNotContain("SaveSearch", s);
    }
}
