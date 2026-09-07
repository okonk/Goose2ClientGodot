using System;

namespace MapEditor.Rendering;

public readonly record struct AppearanceAvailability(bool IsAvailable, string? Diagnostic)
{
    public static AppearanceAvailability Available { get; } = new(true, null);

    public static AppearanceAvailability Unavailable(string diagnostic)
        => new(false, string.IsNullOrWhiteSpace(diagnostic) ? throw new ArgumentException("Diagnostic is required.", nameof(diagnostic)) : diagnostic);
}

public sealed class AppearanceAssetCatalog
{
    private readonly AppearanceManifest _manifest;
    private readonly SpriteAssetCache _cache;

    public AppearanceAssetCatalog(AppearanceManifest manifest, SpriteAssetCache cache)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public SpriteAssetCache Cache => _cache;

    public bool TryResolve(AppearancePartKind kind, int id, int bodyState, out SpriteResolution resolution)
    {
        if (_manifest.TryGetReference(kind, id, bodyState, out SpriteReference reference))
        {
            resolution = _cache.Resolve(reference);
            return resolution.Status is SpriteResolutionStatus.Ready;
        }

        resolution = new SpriteResolution(
            SpriteResolutionStatus.UnknownGraphic,
            default,
            null,
            default,
            $"{kind} part {id} is not declared in the appearance manifest.");
        return false;
    }
}
