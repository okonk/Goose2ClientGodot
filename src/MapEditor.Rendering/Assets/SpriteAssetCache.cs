using System;
using System.Collections.Generic;
using System.IO;

namespace MapEditor.Rendering;

public sealed class SpriteAssetCache : IDisposable
{
    private const int UnavailableMaxFrameSize = 32;

    private readonly ISpriteSheetLoader? _loader;
    private readonly Dictionary<int, SpriteSheetEntry> _sheets = new();
    private readonly Dictionary<SpriteReference, SpriteResolution> _resolutions = new();
    private readonly List<ISpriteSheetImage> _ownedImages = new();
    private bool _disposed;

    public string AssetDirectory { get; }
    public SpriteManifest? Manifest { get; }
    public bool IsAvailable { get; }
    public int MaxFrameWidth { get; }
    public int MaxFrameHeight { get; }
    public bool IsDisposed => _disposed;

    public SpriteAssetCache(string assetDirectory, SpriteManifest manifest, ISpriteSheetLoader loader)
        : this(
            string.IsNullOrWhiteSpace(assetDirectory)
                ? throw new ArgumentException("Asset directory is required.", nameof(assetDirectory))
                : Path.GetFullPath(assetDirectory),
            manifest ?? throw new ArgumentNullException(nameof(manifest)),
            loader ?? throw new ArgumentNullException(nameof(loader)),
            available: true)
    {
    }

    public static SpriteAssetCache CreateUnavailable()
        => new(string.Empty, null, null, available: false);

    private SpriteAssetCache(string assetDirectory, SpriteManifest? manifest, ISpriteSheetLoader? loader, bool available)
    {
        AssetDirectory = assetDirectory;
        Manifest = manifest;
        IsAvailable = available;
        MaxFrameWidth = manifest?.MaxFrameWidth ?? UnavailableMaxFrameSize;
        MaxFrameHeight = manifest?.MaxFrameHeight ?? UnavailableMaxFrameSize;
        _loader = loader;
    }

    public static SpriteAssetCache Open(string assetDirectory, ISpriteSheetLoader loader)
    {
        SpriteManifest manifest = SpriteManifest.Load(assetDirectory);
        return new SpriteAssetCache(assetDirectory, manifest, loader);
    }

    public SpriteResolution Resolve(SpriteReference reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (reference.IsEmpty)
        {
            return new SpriteResolution(SpriteResolutionStatus.Empty, reference, null, default, null);
        }

        if (!IsAvailable)
        {
            return new SpriteResolution(
                SpriteResolutionStatus.AssetsUnavailable,
                reference,
                null,
                default,
                $"Sprite assets are unavailable ({ReferenceText(reference)}); load an asset directory to resolve sprites.");
        }

        SpriteManifest manifest = Manifest!;

        if (_resolutions.TryGetValue(reference, out SpriteResolution cached))
        {
            return cached;
        }

        SpriteResolution resolution;

        if (!manifest.ContainsSheet(reference.Sheet))
        {
            resolution = new SpriteResolution(
                SpriteResolutionStatus.UnknownSheet,
                reference,
                null,
                default,
                $"Sheet {reference.Sheet} is not declared in the manifest ({ReferenceText(reference)}, sheet path {SheetPath(reference.Sheet)}).");
        }
        else if (!manifest.TryGetSourceRect(reference, out SpriteSourceRect sourceRect))
        {
            resolution = new SpriteResolution(
                SpriteResolutionStatus.UnknownGraphic,
                reference,
                null,
                default,
                $"Graphic {reference.Graphic} is not declared in sheet {reference.Sheet} ({ReferenceText(reference)}, sheet path {SheetPath(reference.Sheet)}).");
        }
        else
        {
            SpriteSheetEntry entry = GetSheetEntry(reference.Sheet);
            resolution = ResolveFrame(reference, sourceRect, entry);
        }

        _resolutions[reference] = resolution;
        return resolution;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? firstFailure = null;

        foreach (ISpriteSheetImage image in _ownedImages)
        {
            try
            {
                image.Dispose();
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }

        if (firstFailure is not null)
        {
            throw firstFailure;
        }
    }

    private SpriteSheetEntry GetSheetEntry(int sheet)
    {
        if (_sheets.TryGetValue(sheet, out SpriteSheetEntry existing))
        {
            return existing;
        }

        string path = SheetPath(sheet);
        SpriteSheetLoadResult result = _loader!.Load(path);
        SpriteSheetEntry entry = ValidateAndStore(path, result);
        _sheets[sheet] = entry;
        return entry;
    }

    private SpriteSheetEntry ValidateAndStore(string path, SpriteSheetLoadResult result)
    {
        if (result.Status is SpriteSheetLoadStatus.Success)
        {
            if (result.Image is null || !string.IsNullOrEmpty(result.Diagnostic))
            {
                result.Image?.Dispose();
                throw new InvalidOperationException($"Loader returned a malformed success result for {path}.");
            }

            int width = result.Image.PixelWidth;
            int height = result.Image.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                result.Image.Dispose();
                return new SpriteSheetEntry(
                    null,
                    SpriteSheetLoadStatus.InvalidData,
                    $"Sheet image has invalid dimensions {width}x{height} at {path}.");
            }

            _ownedImages.Add(result.Image);
            return new SpriteSheetEntry(result.Image, SpriteSheetLoadStatus.Success, null);
        }

        if (result.Image is not null || string.IsNullOrEmpty(result.Diagnostic))
        {
            result.Image?.Dispose();
            throw new InvalidOperationException($"Loader returned a malformed {result.Status} result for {path}.");
        }

        return new SpriteSheetEntry(null, result.Status, result.Diagnostic);
    }

    private SpriteResolution ResolveFrame(SpriteReference reference, SpriteSourceRect sourceRect, SpriteSheetEntry entry)
    {
        if (entry.Image is null)
        {
            SpriteResolutionStatus status = entry.Status is SpriteSheetLoadStatus.NotFound
                ? SpriteResolutionStatus.MissingSheetFile
                : SpriteResolutionStatus.SheetLoadFailed;
            string verb = status is SpriteResolutionStatus.MissingSheetFile ? "not found" : "failed to load";
            return new SpriteResolution(
                status,
                reference,
                null,
                default,
                $"Sheet {verb} at {SheetPath(reference.Sheet)} ({ReferenceText(reference)}): {entry.Diagnostic}.");
        }

        long right = (long)sourceRect.X + sourceRect.Width;
        long bottom = (long)sourceRect.Y + sourceRect.Height;

        if (right > entry.Image.PixelWidth || bottom > entry.Image.PixelHeight)
        {
            return new SpriteResolution(
                SpriteResolutionStatus.FrameOutsideSheet,
                reference,
                null,
                default,
                $"Frame extends beyond sheet {SheetPath(reference.Sheet)} ({ReferenceText(reference)}: frame right {right}, bottom {bottom}; sheet {entry.Image.PixelWidth}x{entry.Image.PixelHeight}).");
        }

        return new SpriteResolution(SpriteResolutionStatus.Ready, reference, entry.Image, sourceRect, null);
    }

    private string SheetPath(int sheet)
        => Path.Combine(AssetDirectory, "sheets", $"{sheet}.png");

    private static string ReferenceText(SpriteReference reference)
        => $"reference sheet {reference.Sheet} graphic {reference.Graphic}";

    private readonly record struct SpriteSheetEntry(ISpriteSheetImage? Image, SpriteSheetLoadStatus Status, string? Diagnostic);
}
