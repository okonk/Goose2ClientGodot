using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapEditor.App.Controls;
using Xunit;

namespace MapEditor.App.Tests;

public class SearchPickerControlTests
{
    private sealed class Item
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Filename { get; init; } = string.Empty;
    }

    private sealed record Harness(Window Window, SearchPickerControl<Item> Picker, IReadOnlyList<Item> Items) : IDisposable
    {
        public ListBox List => Picker.GetVisualDescendants().OfType<ListBox>().Single();

        public void Dispose()
        {
            if (Window.IsVisible)
            {
                Window.Close();
            }

            Dispatcher.UIThread.RunJobs();
        }

        public static Harness Create(IReadOnlyList<Item> items)
        {
            var picker = new SearchPickerControl<Item>
            {
                Items = items,
                ItemText = item => $"{item.Id}  {item.Name}  {item.Filename}"
            };
            var window = new Window { Content = picker, Width = 400, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return new Harness(window, picker, items);
        }
    }

    private static Item Make(int id, string name, string filename) => new() { Id = id, Name = name, Filename = filename };

    private static IReadOnlyList<Item> Catalog()
    {
        var items = new List<Item>
        {
            Make(20, "Cave", "cave.bytes"),
            Make(10, "Dungeon", "dungeon.bytes"),
            Make(30, "Tower", "tower.bytes"),
            Make(40, "Ruins", "cave_ruins.bytes")
        };
        return items.AsReadOnly();
    }

    private static string[] VisibleTexts(Harness harness)
        => harness.List.Items.OfType<ListBoxItem>().Select(entry => (string)entry.Content).ToArray();

    private static void Type(Harness harness, string text)
    {
        harness.Picker.Focus();
        Dispatcher.UIThread.RunJobs();
        foreach (char character in text)
        {
            harness.Window.KeyTextInput(character.ToString());
        }

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Filtering_MatchesNameIdAndFilenameOrdinalIgnoreCase()
    {
        using Harness harness = Harness.Create(Catalog());

        Type(harness, "cave");
        Assert.Equal(new[] { "20  Cave  cave.bytes", "40  Ruins  cave_ruins.bytes" }, VisibleTexts(harness));

        harness.Picker.SearchText = string.Empty;
        Type(harness, "CAVE");
        Assert.Equal(new[] { "20  Cave  cave.bytes", "40  Ruins  cave_ruins.bytes" }, VisibleTexts(harness));

        harness.Picker.SearchText = string.Empty;
        Type(harness, "30");
        Assert.Equal(new[] { "30  Tower  tower.bytes" }, VisibleTexts(harness));

        harness.Picker.SearchText = string.Empty;
        Type(harness, "dungeon.bytes");
        Assert.Equal(new[] { "10  Dungeon  dungeon.bytes" }, VisibleTexts(harness));
    }

    [AvaloniaFact]
    public void Filtering_KeepsSourceOrderAndLeavesItemsUntouched()
    {
        IReadOnlyList<Item> catalog = Catalog();
        using Harness harness = Harness.Create(catalog);

        Assert.Equal(new[] { "20  Cave  cave.bytes", "10  Dungeon  dungeon.bytes", "30  Tower  tower.bytes", "40  Ruins  cave_ruins.bytes" },
            VisibleTexts(harness));

        Type(harness, "a");
        Assert.Equal(new[] { "20  Cave  cave.bytes", "40  Ruins  cave_ruins.bytes" }, VisibleTexts(harness));

        Assert.Equal(4, catalog.Count);
        Assert.Equal(20, catalog[0].Id);
        Assert.Equal(40, catalog[3].Id);
    }

    [AvaloniaFact]
    public void Keyboard_UpDownEnter_ReturnsTheOriginalTypedItem()
    {
        using Harness harness = Harness.Create(Catalog());
        Item original = harness.Items[1];

        harness.Picker.Focus();
        Dispatcher.UIThread.RunJobs();
        harness.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(1, harness.List.SelectedIndex);
        harness.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(2, harness.List.SelectedIndex);
        harness.Window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.Equal(1, harness.List.SelectedIndex);
        harness.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Same(original, harness.Picker.SelectedItem);
    }

    [AvaloniaFact]
    public void Escape_ClearsSearchAndKeepsTheSelection()
    {
        using Harness harness = Harness.Create(Catalog());
        Item original = harness.Items[1];
        harness.Picker.Focus();
        Dispatcher.UIThread.RunJobs();
        harness.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Same(original, harness.Picker.SelectedItem);

        Type(harness, "zzz");
        Assert.Empty(VisibleTexts(harness));
        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(string.Empty, harness.Picker.SearchText);
        Assert.Equal(4, harness.List.Items.Count);
        Assert.Same(original, harness.Picker.SelectedItem);
        Assert.Equal(1, harness.List.SelectedIndex);
    }

    [AvaloniaFact]
    public void EmptyResults_EnterCommitsNothing()
    {
        using Harness harness = Harness.Create(Catalog());

        Type(harness, "zzz");
        Assert.Empty(VisibleTexts(harness));
        harness.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Null(harness.Picker.SelectedItem);
    }

    [AvaloniaFact]
    public void Selection_IsPreservedWhileFilteredOut()
    {
        using Harness harness = Harness.Create(Catalog());
        Item original = harness.Items[1];
        harness.Picker.Focus();
        Dispatcher.UIThread.RunJobs();
        harness.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Type(harness, "tower");
        Assert.Single(VisibleTexts(harness));
        Assert.Same(original, harness.Picker.SelectedItem);

        harness.Picker.SearchText = string.Empty;
        Assert.Equal(4, harness.List.Items.Count);
        Assert.Equal(1, harness.List.SelectedIndex);
    }

    [AvaloniaFact]
    public void Focus_MovesToTheSearchBox()
    {
        using Harness harness = Harness.Create(Catalog());

        harness.Picker.Focus();
        Dispatcher.UIThread.RunJobs();

        var focused = harness.Picker.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.True(focused.IsFocused);
    }

    [AvaloniaFact]
    public void Tab_MovesFocusOutOfTheControl()
    {
        var picker = new SearchPickerControl<Item>
        {
            Items = Catalog(),
            ItemText = item => $"{item.Id}  {item.Name}  {item.Filename}"
        };
        var sibling = new TextBox { Width = 100 };
        var window = new Window
        {
            Content = new StackPanel { Children = { picker, sibling } },
            Width = 400,
            Height = 400
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        picker.Focus();
        Dispatcher.UIThread.RunJobs();
        var searchBox = picker.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.True(searchBox.IsFocused);

        for (int i = 0; i < 5; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.False(searchBox.IsFocused);
        Assert.True(sibling.IsFocused);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
