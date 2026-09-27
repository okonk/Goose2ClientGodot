using System;
using System.Collections.Generic;
using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class LogViewerLayoutTests
{
    private const float Tolerance = 0.01f;
    private static readonly Vector2 MinCanvas = new(620f, 340f);
    private static readonly Vector2 DesignCanvas = new(1000f, 620f);
    private static readonly Vector2 HighDpiCanvas = new(2000f, 1240f);

    [Fact]
    public void DesignSize_Is1000x620()
        => Assert.Equal(new Vector2(1000f, 620f), LogViewerLayout.DesignSize);

    [Fact]
    public void MinSize_Is620x340()
        => Assert.Equal(new Vector2(620f, 340f), LogViewerLayout.MinSize);

    [Fact]
    public void Margins_ArePinned()
    {
        Assert.Equal(8f, LogViewerLayout.Margin);
        Assert.Equal(24f, LogViewerLayout.TitleBarHeight);
        Assert.Equal(96f, LogViewerLayout.SuggestionMaxHeight);
        Assert.Equal(120f, LogViewerLayout.FilterAreaHeight);
    }

    [Fact]
    public void ColumnMinimums_ArePinnedInServerOrder()
        => Assert.Equal(new[] { 150f, 95f, 70f, 70f, 85f, 90f }, LogViewerLayout.ColumnMinimums);

    [Fact]
    public void ColumnExpandRatios_ArePinned()
        => Assert.Equal(new[] { 0f, 1.0f, 0.5f, 0.5f, 1.0f, 4.0f }, LogViewerLayout.ColumnExpandRatios);

    [Theory]
    [MemberData(nameof(Canvases))]
    public void SplitPanels_ArePinnedPerCanvas(Vector2 canvas, float expectedResults, float expectedDetails)
    {
        var (results, details) = LogViewerLayout.SplitPanels(canvas);
        Assert.True(Math.Abs(results - expectedResults) < Tolerance, $"results {results}");
        Assert.True(Math.Abs(details - expectedDetails) < Tolerance, $"details {details}");
    }

    public static IEnumerable<object[]> Canvases()
    {
        yield return new object[] { MinCanvas, 350.32f, 253.68f };
        yield return new object[] { DesignCanvas, 570.72f, 413.28f };
        yield return new object[] { HighDpiCanvas, 1150.72f, 833.28f };
    }

    [Fact]
    public void ColumnWidths_AtMinimumCanvas_HoldEveryMinimum()
    {
        var widths = LogViewerLayout.ColumnWidths(LogViewerLayout.ResultsWidth(MinCanvas));
        for (int i = 0; i < 6; i++)
            Assert.True(Math.Abs(widths[i] - LogViewerLayout.ColumnMinimums[i]) < Tolerance, $"column {i}: {widths[i]}");
    }

    [Fact]
    public void ColumnWidths_AtDesignCanvas_DistributeExtraSpaceByRatio()
        => AssertClose(
            LogViewerLayout.ColumnWidths(LogViewerLayout.ResultsWidth(DesignCanvas)),
            new[] { 150f, 96.53143f, 70.76571f, 70.76571f, 86.53143f, 96.12571f });

    [Fact]
    public void ColumnWidths_AtHighDpiCanvas_DistributeExtraSpaceByRatio()
        => AssertClose(
            LogViewerLayout.ColumnWidths(LogViewerLayout.ResultsWidth(HighDpiCanvas)),
            new[] { 150f, 179.38857f, 112.19429f, 112.19429f, 169.38857f, 427.55429f });

    [Fact]
    public void ColumnWidths_NeverFallBelowMinimums()
    {
        foreach (var canvas in new[] { MinCanvas, DesignCanvas, HighDpiCanvas, new Vector2(620f, 900f) })
        {
            var widths = LogViewerLayout.ColumnWidths(LogViewerLayout.ResultsWidth(canvas));
            for (int i = 0; i < 6; i++)
                Assert.True(widths[i] >= LogViewerLayout.ColumnMinimums[i], $"column {i} at {canvas}");
        }
    }

    [Fact]
    public void MinimumCanvas_LeavesRoomForResultsAndControls()
    {
        float contentHeight = LogViewerLayout.MinSize.Y - LogViewerLayout.TitleBarHeight;
        Assert.True(LogViewerLayout.FilterAreaHeight + LogViewerLayout.Margin < contentHeight);
        Assert.True(contentHeight - LogViewerLayout.FilterAreaHeight - LogViewerLayout.Margin > 0f);
    }

    private static void AssertClose(float[] actual, float[] expected)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(actual[i] - expected[i]) < Tolerance, $"index {i}: {actual[i]}");
    }
}
