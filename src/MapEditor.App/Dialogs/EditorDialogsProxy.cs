using System;
using System.Threading.Tasks;

namespace MapEditor.App.Dialogs;

// The window owns the dialogs and the dialogs own the window; the proxy breaks that cycle
// until the window exists, at which point App wires the real implementation in.
internal sealed class EditorDialogsProxy : IEditorDialogs
{
    private IEditorDialogs? _target;

    public IEditorDialogs Target
    {
        get => _target ?? throw new InvalidOperationException("Target has not been set.");
        set => _target = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Task<NewMapRequest?> ShowNewMapAsync() => Target.ShowNewMapAsync();

    public Task<DirtyChoice> ShowDirtyAsync(string displayName) => Target.ShowDirtyAsync(displayName);

    public Task<ExternalChangeChoice> ShowExternalChangeAsync(string path) => Target.ShowExternalChangeAsync(path);

    public Task<string?> PickOpenMapAsync() => Target.PickOpenMapAsync();

    public Task<string?> PickSaveMapAsync(string suggestedName) => Target.PickSaveMapAsync(suggestedName);

    public Task<string?> PickAssetDirectoryAsync() => Target.PickAssetDirectoryAsync();

    public Task ShowErrorAsync(ErrorPresentation error) => Target.ShowErrorAsync(error);
}
