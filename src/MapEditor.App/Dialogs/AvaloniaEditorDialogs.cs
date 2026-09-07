using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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

    public Task<MapTileRectangle?> ShowResizeMapAsync(MapDocument document, Func<MapTileRectangle, MapResizePlan> plan) => new ResizeMapDialog(document, plan).ShowDialog<MapTileRectangle?>(_owner);

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
        => new SpreadsheetDialog(prefill).ShowDialog<string?>(_owner);

    public Task<MapReference?> ShowMapConfirmationAsync(IReadOnlyList<MapReference> maps, MapReference? suggested, string documentName)
        => new MapReferenceDialog(maps, suggested, documentName).ShowDialog<MapReference?>(_owner);

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

    private IStorageProvider StorageProvider => TopLevel.GetTopLevel(_owner)!.StorageProvider;

    private static string? GetLocalPath(IStorageItem item)
    {
        Uri uri = item.Path;
        return uri.IsFile ? uri.LocalPath : null;
    }
}
