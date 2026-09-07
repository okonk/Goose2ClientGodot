using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MapEditor.Core;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;

namespace MapEditor.App.Dialogs;

internal sealed class AvaloniaEditorDialogs : IEditorDialogs
{
    private static readonly FilePickerFileType[] MapFileTypes =
    {
        new("Map files") { Patterns = new[] { "*.bytes" } },
        new("All files") { Patterns = new[] { "*" } }
    };

    private readonly Window _owner;

    public AvaloniaEditorDialogs(Window owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public Task<NewMapRequest?> ShowNewMapAsync() => new NewMapDialog().ShowDialog<NewMapRequest?>(_owner);

    public Task<MapTileRectangle?> ShowResizeMapAsync(MapDocument document) => new ResizeMapDialog(document).ShowDialog<MapTileRectangle?>(_owner);

    public Task<DirtyChoice> ShowDirtyAsync(string displayName) => new ChoiceDialog(
            "Unsaved changes",
            $"Save changes to '{displayName}' before continuing?",
            "Save",
            DirtyChoice.Save,
            "Discard",
            DirtyChoice.Discard,
            "Cancel",
            DirtyChoice.Cancel)
        .ShowDialog<DirtyChoice>(_owner);

    public Task<ExternalChangeChoice> ShowExternalChangeAsync(string path) => new ChoiceDialog(
            "Map changed",
            $"'{path}' has been modified by another program. Overwrite it?",
            "Overwrite",
            ExternalChangeChoice.Overwrite,
            "Save As",
            ExternalChangeChoice.SaveAs,
            "Cancel",
            ExternalChangeChoice.Cancel)
        .ShowDialog<ExternalChangeChoice>(_owner);

    public async Task<string?> PickOpenMapAsync()
    {
        IReadOnlyList<IStorageItem?>? items = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open map",
            FileTypeFilter = MapFileTypes
        });

        if (items is null || items.Count == 0 || items[0] is not IStorageFile file)
        {
            return null;
        }

        string? path = GetLocalPath(file);
        file.Dispose();
        return path;
    }

    public async Task<string?> PickSaveMapAsync(string suggestedName)
    {
        string suggested = Path.GetExtension(suggestedName).Length == 0 ? suggestedName + ".bytes" : suggestedName;
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save map",
            SuggestedFileName = suggested,
            FileTypeChoices = MapFileTypes,
            DefaultExtension = "bytes",
            ShowOverwritePrompt = true
        });

        if (file is null)
        {
            return null;
        }

        string? path = GetLocalPath(file);
        file.Dispose();
        return path;
    }

    public async Task<string?> PickAssetDirectoryAsync()
    {
        IReadOnlyList<IStorageItem?>? items = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select asset directory"
        });

        if (items is null || items.Count == 0 || items[0] is not IStorageFolder folder)
        {
            return null;
        }

        string? path = GetLocalPath(folder);
        folder.Dispose();
        return path;
    }

    public Task ShowErrorAsync(ErrorPresentation error) => new ErrorDialog(error.Title, error.Message).ShowDialog(_owner);

    public Task<string?> ShowSpreadsheetUrlAsync(string? prefill)
    {
        var input = new TextBox
        {
            Watermark = "Paste the Google spreadsheet URL",
            Text = prefill,
            MinWidth = 420
        };
        var ok = new Button { Content = "Pull", MinWidth = 84, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        var window = BuildDialogWindow("Spreadsheet", new TextBlock
        {
            Text = "Which spreadsheet holds this map's game data?",
            TextWrapping = TextWrapping.Wrap
        }, input, ok, cancel);
        ok.Click += (_, _) => window.Close(input.Text);
        cancel.Click += (_, _) => window.Close();
        return window.ShowDialog<string?>(_owner);
    }

    public Task<MapReference?> ShowMapConfirmationAsync(IReadOnlyList<MapReference> maps, MapReference? suggested, string documentName)
    {
        var list = new ListBox { MinWidth = 420, MinHeight = 240 };
        int selectedIndex = -1;
        for (int i = 0; i < maps.Count; i++)
        {
            MapReference map = maps[i];
            bool isSuggested = suggested.HasValue && suggested.Value.Equals(map);
            list.Items.Add(new ListBoxItem
            {
                Content = $"{map.MapId}  {map.MapName}  {map.MapFilename}{(isSuggested ? "  (suggested)" : string.Empty)}",
                Tag = map
            });
            if (isSuggested)
            {
                selectedIndex = i;
            }
        }

        if (selectedIndex < 0 && maps.Count > 0)
        {
            selectedIndex = 0;
        }

        list.SelectedIndex = selectedIndex;
        var ok = new Button { Content = "Confirm", MinWidth = 84, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        var window = BuildDialogWindow($"Confirm map for '{documentName}'", new TextBlock
        {
            Text = "Confirm the spreadsheet row that holds this map's game data.",
            TextWrapping = TextWrapping.Wrap
        }, list, ok, cancel);
        ok.Click += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: MapReference reference })
            {
                window.Close(reference);
            }
            else
            {
                window.Close();
            }
        };
        cancel.Click += (_, _) => window.Close();
        return window.ShowDialog<MapReference?>(_owner);
    }

    public Task<SheetDirtyChoice> ShowSheetDirtyAsync(string documentName) => new ChoiceDialog(
            "Unpushed game data",
            $"Push local game data changes for '{documentName}' before continuing?",
            "Push",
            SheetDirtyChoice.Push,
            "Discard",
            SheetDirtyChoice.Discard,
            "Cancel",
            SheetDirtyChoice.Cancel)
        .ShowDialog<SheetDirtyChoice>(_owner);

    public Task<PushConflictChoice> ShowPushConflictAsync(string documentName) => new ChoiceDialog(
            "Remote game data changed",
            $"The spreadsheet rows for '{documentName}' changed after this tab pulled them. What should happen?",
            "Overwrite",
            PushConflictChoice.Overwrite,
            "Pull Instead",
            PushConflictChoice.PullInstead,
            "Cancel",
            PushConflictChoice.Cancel)
        .ShowDialog<PushConflictChoice>(_owner);

    private static Window BuildDialogWindow(string title, Control header, Control body, Button ok, Button cancel)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 16 };
        panel.Children.Add(header);
        panel.Children.Add(body);
        panel.Children.Add(buttons);
        return new Window
        {
            Title = title,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
    }

    private IStorageProvider StorageProvider => TopLevel.GetTopLevel(_owner)!.StorageProvider;

    private static string? GetLocalPath(IStorageItem item)
    {
        Uri uri = item.Path;
        return uri.IsFile ? uri.LocalPath : null;
    }
}
