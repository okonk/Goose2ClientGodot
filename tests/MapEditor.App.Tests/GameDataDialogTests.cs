using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using MapEditor.App.Dialogs;
using MapEditor.GameData.Rows;
using Xunit;

namespace MapEditor.App.Tests;

public class GameDataDialogTests
{
    private static readonly MapReference Map10 = new(10, "Dungeon", "dungeon.map");
    private static readonly MapReference Map20 = new(20, "Cave", "cave.map");
    private static readonly MapReference Map30 = new(30, "Tower", "tower.map");
    private static readonly IReadOnlyList<MapReference> Maps = new[] { Map10, Map20, Map30 };

    // Rows carry an ellipsizing TextBlock rather than a bare string.
    private static string RowText(ListBoxItem entry) => ((TextBlock)entry.Content!).Text ?? string.Empty;

    private sealed class Owner : IDisposable
    {
        public Window Window { get; } = new();

        public Owner()
        {
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            if (Window.IsVisible)
            {
                Window.Close();
            }

            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task Spreadsheet_Pull_ClosesWithTheTypedUrl()
    {
        using Owner owner = new();
        var dialog = new SpreadsheetDialog(null);
        Task<string?> result = dialog.ShowDialog<string?>(owner.Window);
        Dispatcher.UIThread.RunJobs();

        Assert.True(dialog.IsVisible);
        dialog.FindControl<TextBox>("UrlBox")!.Text = "https://docs.google.com/spreadsheets/d/abc123";
        dialog.FindControl<Button>("PullButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(dialog.IsVisible);
        Assert.Equal("https://docs.google.com/spreadsheets/d/abc123", await result);
    }

    [AvaloniaFact]
    public void Spreadsheet_Prefill_PopulatesTheUrlBox()
    {
        using Owner owner = new();
        var dialog = new SpreadsheetDialog("https://docs.google.com/spreadsheets/d/xyz");

        Assert.Equal("https://docs.google.com/spreadsheets/d/xyz", dialog.FindControl<TextBox>("UrlBox")!.Text);
    }

    [AvaloniaFact]
    public async Task Spreadsheet_Cancel_ReturnsNull()
    {
        using Owner owner = new();
        var dialog = new SpreadsheetDialog(null);
        Task<string?> result = dialog.ShowDialog<string?>(owner.Window);
        Dispatcher.UIThread.RunJobs();

        dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(dialog.IsVisible);
        Assert.Null(await result);
    }

    [AvaloniaFact]
    public async Task Spreadsheet_WindowClose_ReturnsNull()
    {
        using Owner owner = new();
        var dialog = new SpreadsheetDialog(null);
        Task<string?> result = dialog.ShowDialog<string?>(owner.Window);
        Dispatcher.UIThread.RunJobs();

        dialog.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(dialog.IsVisible);
        Assert.Null(await result);
    }

    [AvaloniaFact]
    public void Map_SuggestedRow_IsDistinguishedWithoutAutoConfirming()
    {
        using Owner owner = new();
        var dialog = new MapReferenceDialog(Maps, Map20, "dungeon.map");
        Task<MapReference?> result = dialog.ShowDialog<MapReference?>(owner.Window);
        Dispatcher.UIThread.RunJobs();

        Assert.True(dialog.IsVisible);
        Assert.False(result.IsCompleted);

        ListBox list = dialog.FindControl<ListBox>("MapList")!;
        ListBoxItem[] entries = list.Items.OfType<ListBoxItem>().ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Contains("(suggested)", RowText(entries[1]));
        Assert.Contains("suggested", entries[1].Classes);
        Assert.DoesNotContain("(suggested)", RowText(entries[0]));
        Assert.Equal(1, list.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task Map_Confirm_ReturnsTheSelectedReference()
    {
        using Owner owner = new();
        var dialog = new MapReferenceDialog(Maps, Map20, "dungeon.map");
        Task<MapReference?> result = dialog.ShowDialog<MapReference?>(owner.Window);
        Dispatcher.UIThread.RunJobs();

        dialog.FindControl<ListBox>("MapList")!.SelectedIndex = 2;
        dialog.FindControl<Button>("ConfirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(dialog.IsVisible);
        Assert.Equal(Map30, await result);
    }

    [AvaloniaFact]
    public void Spreadsheet_LongUrl_DoesNotGrowTheWindow()
    {
        using Owner owner = new();
        var dialog = new SpreadsheetDialog(null);
        Task<string?> result = dialog.ShowDialog<string?>(owner.Window);
        Dispatcher.UIThread.RunJobs();

        double initialWidth = dialog.Bounds.Size.Width;
        dialog.FindControl<TextBox>("UrlBox")!.Text = "https://docs.google.com/spreadsheets/d/1abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuv/edit#gid=0";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(initialWidth, dialog.Bounds.Size.Width);
        Assert.Equal(456, dialog.Width);
    }

    [AvaloniaFact]
    public void Map_ManyMaps_WindowStaysBounded_WithConfirmButtonVisible()
    {
        using Owner owner = new();
        var manyMaps = Enumerable.Range(0, 200)
            .Select(i => new MapReference(i, $"Map {i}", $"map{i}.map"))
            .ToList();
        var dialog = new MapReferenceDialog(manyMaps, null, "dungeon.map");
        Task<MapReference?> result = dialog.ShowDialog<MapReference?>(owner.Window);
        Dispatcher.UIThread.RunJobs();

        Assert.InRange(dialog.Bounds.Size.Height, 0, 500);

        ScrollViewer listScroller = dialog.FindControl<ListBox>("MapList")!.GetVisualDescendants().OfType<ScrollViewer>().First();
        ScrollBar listScrollbar = listScroller.GetVisualDescendants().OfType<ScrollBar>().First(bar => bar.Orientation == Orientation.Vertical);
        Assert.True(listScrollbar.IsVisible);

        Button confirm = dialog.FindControl<Button>("ConfirmButton")!;
        Point? confirmBottomRight = confirm.TranslatePoint(confirm.Bounds.BottomRight, dialog);
        Assert.NotNull(confirmBottomRight);
        Assert.InRange(confirmBottomRight.Value.Y, 0, dialog.Bounds.Size.Height + 1);
    }

    [AvaloniaFact]
    public void Map_WithoutSuggestion_PreselectsTheFirstRow()
    {
        using Owner owner = new();
        var dialog = new MapReferenceDialog(Maps, null, "dungeon.map");

        Assert.Equal(0, dialog.FindControl<ListBox>("MapList")!.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task Map_Cancel_ReturnsNull()
    {
        using Owner owner = new();
        var dialog = new MapReferenceDialog(Maps, Map20, "dungeon.map");
        Task<MapReference?> result = dialog.ShowDialog<MapReference?>(owner.Window);
        Dispatcher.UIThread.RunJobs();

        dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(dialog.IsVisible);
        Assert.Null(await result);
    }

    [AvaloniaFact]
    public async Task Map_WindowClose_ReturnsNull()
    {
        using Owner owner = new();
        var dialog = new MapReferenceDialog(Maps, Map20, "dungeon.map");
        Task<MapReference?> result = dialog.ShowDialog<MapReference?>(owner.Window);
        Dispatcher.UIThread.RunJobs();

        dialog.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(dialog.IsVisible);
        Assert.Null(await result);
    }
}
