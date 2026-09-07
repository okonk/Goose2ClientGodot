using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MapEditor.Core;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;

namespace MapEditor.App.Dialogs;

internal enum DirtyChoice
{
    Save,
    Discard,
    Cancel
}

internal enum ExternalChangeChoice
{
    Overwrite,
    SaveAs,
    Cancel
}

internal enum SheetDirtyChoice
{
    Push,
    Discard,
    Cancel
}

internal sealed record NewMapRequest(int Width, int Height);

internal interface IEditorDialogs
{
    Task<NewMapRequest?> ShowNewMapAsync();

    Task<MapTileRectangle?> ShowResizeMapAsync(MapDocument document, Func<MapTileRectangle, MapResizePlan> plan);

    Task<DirtyChoice> ShowDirtyAsync(string displayName);

    Task<ExternalChangeChoice> ShowExternalChangeAsync(string path);

    Task<string?> PickOpenMapAsync();

    Task<string?> PickSaveMapAsync(string suggestedName);

    Task<string?> PickAssetDirectoryAsync();

    Task ShowErrorAsync(ErrorPresentation error);

    Task<string?> ShowSpreadsheetUrlAsync(string? prefill);

    Task<MapReference?> ShowMapConfirmationAsync(IReadOnlyList<MapReference> maps, MapReference? suggested, string documentName);

    Task<SheetDirtyChoice> ShowSheetDirtyAsync(string documentName);

    Task<PushConflictChoice> ShowPushConflictAsync(string documentName);
}
