using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Terrain;

public sealed class TerrainCatalogFileStore
{
    private const string TempPrefix = ".terrain-";
    private const string TempSuffix = ".tmp";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ITerrainCatalogFileOperations _operations;

    public TerrainCatalogFileStore()
        : this(new TerrainCatalogFileOperations())
    {
    }

    internal TerrainCatalogFileStore(ITerrainCatalogFileOperations operations)
    {
        _operations = operations;
    }

    public TerrainCatalogLoadResult Open(string assetDirectory, SpriteManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory))
        {
            throw new ArgumentException("Asset directory is required.", nameof(assetDirectory));
        }

        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var sourcePath = Path.Combine(Path.GetFullPath(assetDirectory), TerrainAssetCatalog.FileName);

        if (!_operations.DirectoryExists(assetDirectory))
        {
            return TerrainCatalogLoadResult.Unavailable(sourcePath);
        }

        if (_operations.DirectoryExists(sourcePath))
        {
            return TerrainCatalogLoadResult.Invalid(
                sourcePath,
                TerrainFileRevision.Missing,
                Array.Empty<TerrainValidationIssue>(),
                $"Terrain path is a directory: {sourcePath}");
        }

        if (!_operations.FileExists(sourcePath))
        {
            return TerrainCatalogLoadResult.ValidEmpty(sourcePath);
        }

        byte[] bytes;
        try
        {
            bytes = _operations.ReadFile(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TerrainCatalogLoadResult.Invalid(
                sourcePath,
                TerrainFileRevision.Missing,
                Array.Empty<TerrainValidationIssue>(),
                $"Failed to read terrain file: {sourcePath}: {ex.Message}");
        }

        var revision = TerrainFileRevision.FromBytes(bytes);

        TerrainCatalog catalog;
        try
        {
            catalog = TerrainCatalogJson.Parse(StrictUtf8.GetString(bytes), sourcePath);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException)
        {
            return TerrainCatalogLoadResult.Invalid(
                sourcePath,
                revision,
                Array.Empty<TerrainValidationIssue>(),
                $"Invalid UTF-8 in terrain file: {sourcePath}: {ex.Message}");
        }
        catch (TerrainCatalogFormatException ex)
        {
            return TerrainCatalogLoadResult.Invalid(sourcePath, revision, Array.Empty<TerrainValidationIssue>(), ex.Message);
        }

        var validation = TerrainAssetCatalog.Validate(catalog, manifest);
        if (!validation.IsValid)
        {
            var errors = validation.Issues.Count(issue => issue.Severity == TerrainValidationSeverity.Error);
            var first = validation.Issues.First(issue => issue.Severity == TerrainValidationSeverity.Error);
            return TerrainCatalogLoadResult.Invalid(
                sourcePath,
                revision,
                validation.Issues,
                $"Terrain validation failed ({errors} error(s)) in {sourcePath}: {first.Message}");
        }

        return TerrainCatalogLoadResult.Valid(sourcePath, revision, catalog, validation.Index, validation.Issues);
    }

    public TerrainCatalogPreparedSave PrepareSave(
        string assetDirectory,
        TerrainCatalog catalog,
        SpriteManifest manifest,
        TerrainFileRevision expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory))
        {
            throw new ArgumentException("Asset directory is required.", nameof(assetDirectory));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var validation = TerrainAssetCatalog.Validate(catalog, manifest);
        if (!validation.IsValid)
        {
            throw new TerrainCatalogValidationException(validation.Issues);
        }

        var sourcePath = Path.Combine(Path.GetFullPath(assetDirectory), TerrainAssetCatalog.FileName);
        var bytes = Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(catalog));
        return new TerrainCatalogPreparedSave(
            Guid.NewGuid().ToString("N"),
            bytes,
            catalog,
            validation.Index!,
            sourcePath,
            expectedRevision);
    }

    public TerrainCatalogSaveResult Save(TerrainCatalogPreparedSave prepared)
    {
        var directory = Path.GetDirectoryName(prepared.SourcePath)!;
        var temp = Path.Combine(directory, TempPrefix + prepared.OperationId + TempSuffix);
        var failed = false;

        try
        {
            var canonicalBytes = prepared.CanonicalBytes;
            using (var stream = _operations.CreateNew(temp))
            {
                stream.Write(canonicalBytes);
                _operations.FlushToDisk(stream);
            }

            var actual = _operations.FileExists(prepared.SourcePath)
                ? (TerrainFileRevision?)TerrainFileRevision.FromBytes(_operations.ReadFile(prepared.SourcePath))
                : TerrainFileRevision.Missing;
            if (actual != prepared.ExpectedRevision)
            {
                throw new TerrainExternalChangeException(prepared.SourcePath, prepared.ExpectedRevision, actual);
            }

            // The pre-move revision check and this move are not atomic: a concurrent writer can still
            // replace the destination in the gap, because the supported file APIs provide no portable
            // compare-and-replace.
            _operations.AtomicOverwrite(temp, prepared.SourcePath);
            return new TerrainCatalogSaveResult(
                prepared.OperationId,
                prepared.Catalog,
                prepared.Index,
                TerrainFileRevision.FromBytes(canonicalBytes));
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            try
            {
                if (_operations.FileExists(temp))
                {
                    _operations.Delete(temp);
                }
            }
            catch when (failed)
            {
            }
        }
    }
}
