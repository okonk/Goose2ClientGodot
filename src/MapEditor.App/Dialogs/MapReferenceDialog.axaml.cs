using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MapEditor.GameData.Rows;

namespace MapEditor.App.Dialogs;

internal partial class MapReferenceDialog : Window
{
    public MapReferenceDialog(IReadOnlyList<MapReference> maps, MapReference? suggested, string documentName)
    {
        InitializeComponent();
        Title = $"Confirm map for '{documentName}'";
        Header.Text = "Confirm the spreadsheet row that holds this map's game data.";
        int selectedIndex = -1;
        for (int i = 0; i < maps.Count; i++)
        {
            MapReference map = maps[i];
            bool isSuggested = suggested is { } candidate && candidate.Equals(map);
            var item = new ListBoxItem
            {
                Content = $"{map.MapId}  {map.MapName}  {map.MapFilename}",
                Tag = map
            };
            if (isSuggested)
            {
                item.Content = $"{item.Content}  (suggested)";
                item.Classes.Add("suggested");
                selectedIndex = i;
            }

            MapList.Items.Add(item);
        }

        if (selectedIndex < 0 && maps.Count > 0)
        {
            selectedIndex = 0;
        }

        MapList.SelectedIndex = selectedIndex;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (MapList.SelectedItem is ListBoxItem { Tag: MapReference reference })
        {
            Close(reference);
        }
        else
        {
            Close();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
