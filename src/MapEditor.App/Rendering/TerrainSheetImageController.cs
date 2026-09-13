using System;
using System.IO;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class TerrainSheetImageController : IDisposable
{
    private readonly string _assetDirectory;
    private readonly ISpriteSheetLoader _loader;
    private ISpriteSheetImage? _image;
    private string? _diagnostic;
    private int? _selectedSheet;
    private bool _disposed;

    public TerrainSheetImageController(string assetDirectory, ISpriteSheetLoader loader)
    {
        _assetDirectory = assetDirectory ?? throw new ArgumentNullException(nameof(assetDirectory));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public ISpriteSheetImage? Image => _image;

    public string? Diagnostic => _diagnostic;

    public int? SelectedSheet => _selectedSheet;

    public event EventHandler? ImageChanged;

    public void SelectSheet(int? sheetId)
    {
        if (_disposed || _selectedSheet == sheetId)
        {
            return;
        }

        _selectedSheet = sheetId;
        ISpriteSheetImage? replaced = _image;
        _image = null;
        _diagnostic = null;
        replaced?.Dispose();

        if (sheetId is { } id)
        {
            string path = Path.Combine(_assetDirectory, "sheets", $"{id}.png");
            SpriteSheetLoadResult result = _loader.Load(path);
            if (result.Status is not SpriteSheetLoadStatus.Success || result.Image is null)
            {
                result.Image?.Dispose();
                _diagnostic = result.Diagnostic ?? $"Loader returned a malformed success result for {path}.";
            }
            else
            {
                _image = result.Image;
            }
        }

        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ISpriteSheetImage? image = _image;
        _image = null;
        image?.Dispose();
    }
}
