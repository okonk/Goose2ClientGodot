using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MapEditor.App.Controls;

public class SearchPickerControl<T> : UserControl
{
    private readonly TextBox _searchBox;
    private readonly ListBox _results;
    private readonly TextBlock _selectionText;

    // AVP1002 suppressed: the value types are generic in T, so a non-generic owner type is
    // impossible; none of these properties are set from XAML.
#pragma warning disable AVP1002
    public static readonly StyledProperty<IReadOnlyList<T>> ItemsProperty =
        AvaloniaProperty.Register<SearchPickerControl<T>, IReadOnlyList<T>>(nameof(Items), Array.Empty<T>());

    public static readonly StyledProperty<Func<T, string>> ItemTextProperty =
        AvaloniaProperty.Register<SearchPickerControl<T>, Func<T, string>>(nameof(ItemText));

    public static readonly StyledProperty<T?> SelectedItemProperty =
        AvaloniaProperty.Register<SearchPickerControl<T>, T?>(nameof(SelectedItem));

    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<SearchPickerControl<T>, string>(nameof(SearchText), string.Empty);
#pragma warning restore AVP1002
    public SearchPickerControl()
    {
        Focusable = true;
        var root = (StackPanel)AvaloniaXamlLoader.Load(new Uri("avares://MapEditor.App/Controls/SearchPickerControl.axaml"), null);
        Content = root;
        _searchBox = root.FindControl<TextBox>("SearchBox")!;
        _results = root.FindControl<ListBox>("Results")!;
        _selectionText = root.FindControl<TextBlock>("SelectionText")!;
        _searchBox.TextChanged += (_, _) =>
        {
            if (_searchBox.Text != SearchText)
            {
                SearchText = _searchBox.Text ?? string.Empty;
            }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ItemsProperty || e.Property == ItemTextProperty)
            {
                Rebuild();
            }
            else if (e.Property == SearchTextProperty)
            {
                if (_searchBox.Text != SearchText)
                {
                    _searchBox.Text = SearchText ?? string.Empty;
                }

                Rebuild();
            }
            else if (e.Property == SelectedItemProperty)
            {
                SyncListSelection();
            }
        };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public IReadOnlyList<T> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public Func<T, string> ItemText
    {
        get => GetValue(ItemTextProperty);
        set => SetValue(ItemTextProperty, value);
    }

    public T? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.Source, this) && !_searchBox.IsFocused)
        {
            _searchBox.Focus();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (_results.SelectedItem is ListBoxItem { Tag: T item })
                {
                    SelectedItem = item;
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                if (SearchText.Length > 0)
                {
                    SearchText = string.Empty;
                    e.Handled = true;
                }
                break;
            case Key.Up:
                if (_results.Items.Count > 0)
                {
                    _results.SelectedIndex = Math.Max(0, _results.SelectedIndex - 1);
                    e.Handled = true;
                }
                break;
            case Key.Down:
                if (_results.Items.Count > 0)
                {
                    _results.SelectedIndex = Math.Min(_results.Items.Count - 1, _results.SelectedIndex + 1);
                    e.Handled = true;
                }
                break;
        }
    }

    private void Rebuild()
    {
        string search = SearchText ?? string.Empty;
        Func<T, string> project = ItemText ?? (item => item?.ToString() ?? string.Empty);
        _results.Items.Clear();
        foreach (T item in Items ?? Array.Empty<T>())
        {
            string text = project(item);
            if (text.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                _results.Items.Add(new ListBoxItem { Content = text, Tag = item });
            }
        }

        SyncListSelection();
    }

    private void SyncListSelection()
    {
        if (_results.Items.Count == 0)
        {
            _results.SelectedIndex = -1;
            _selectionText.Text = "—";
            return;
        }

        int index = -1;
        if (SelectedItem is { } selected)
        {
            for (int i = 0; i < _results.Items.Count; i++)
            {
                if (_results.Items[i] is ListBoxItem { Tag: T tag } && Equals(tag, selected))
                {
                    index = i;
                    break;
                }
            }
        }

        _results.SelectedIndex = index < 0 ? 0 : index;
        _selectionText.Text = SelectedItem is { } item && ItemText is { } project ? project(item) : "—";
    }
}
