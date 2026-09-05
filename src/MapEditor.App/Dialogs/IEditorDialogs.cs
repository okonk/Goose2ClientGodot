using System;
using System.Threading.Tasks;
using MapEditor.Core;

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

internal sealed record NewMapRequest(int Width, int Height);

internal interface IEditorDialogs
{
    Task<NewMapRequest?> ShowNewMapAsync();

    Task<MapTileRectangle?> ShowResizeMapAsync(MapDocument document);

    Task<DirtyChoice> ShowDirtyAsync(string displayName);

    Task<ExternalChangeChoice> ShowExternalChangeAsync(string path);

    Task<string?> PickOpenMapAsync();

    Task<string?> PickSaveMapAsync(string suggestedName);

    Task<string?> PickAssetDirectoryAsync();

    Task ShowErrorAsync(ErrorPresentation error);
}
