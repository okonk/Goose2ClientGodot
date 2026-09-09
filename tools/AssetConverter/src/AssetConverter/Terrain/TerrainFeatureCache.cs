using MapEditor.Core.Terrain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Goose2.AssetConverter.Terrain;

public interface ITerrainSheetImageLoader
{
    Image<Rgba32> Load(string path);
}

public sealed class TerrainFeatureCache
{
    private readonly Dictionary<TerrainGraphicReference, TerrainImageFeatures> _features;

    public IReadOnlyList<TerrainGraphicReference> References { get; }

    internal int ComparisonCount { get; private set; }

    internal int SimilarityCount { get; private set; }

    private TerrainFeatureCache(Dictionary<TerrainGraphicReference, TerrainImageFeatures> features)
    {
        _features = features;
        References = features.Keys
            .OrderBy(reference => reference.Sheet)
            .ThenBy(reference => reference.Graphic)
            .ToList()
            .AsReadOnly();
    }

    public static TerrainFeatureCache Build(TerrainCorpus corpus, ITerrainSheetImageLoader? loader = null)
    {
        var imageLoader = loader ?? new ImageSharpTerrainSheetImageLoader();
        var features = new Dictionary<TerrainGraphicReference, TerrainImageFeatures>();
        foreach (var sheet in corpus.RelevantSheets)
        {
            var path = Path.Combine(corpus.Root, TerrainCorpusLoader.SheetsRelativeDirectory, sheet + ".png");
            using var image = imageLoader.Load(path);
            foreach (var frame in corpus.FrameIndex.GetFrames(sheet))
            {
                if (frame.Reference.Graphic == 0
                    || frame.Rect.Width != TerrainImageFeatures.FrameSize
                    || frame.Rect.Height != TerrainImageFeatures.FrameSize)
                {
                    continue;
                }

                ValidateFrameBounds(path, frame, image.Width, image.Height);
                features[frame.Reference] = TerrainFeatureExtractor.Extract(frame.Reference, image, frame.Rect.X, frame.Rect.Y);
            }
        }

        return new TerrainFeatureCache(features);
    }

    public bool TryGetFeatures(TerrainGraphicReference reference, out TerrainImageFeatures features)
        => _features.TryGetValue(reference, out features);

    public double Similarity(TerrainGraphicReference a, TerrainGraphicReference b)
    {
        if (!_features.TryGetValue(a, out var first) || !_features.TryGetValue(b, out var second))
        {
            throw new KeyNotFoundException($"Feature cache is missing {a.Sheet}:{a.Graphic} or {b.Sheet}:{b.Graphic}.");
        }

        SimilarityCount++;
        return TerrainFeatureExtractor.Similarity(first, second);
    }

    public IReadOnlyList<TerrainGraphicReference> QueryCandidates(TerrainGraphicReference reference)
    {
        if (!_features.TryGetValue(reference, out var self))
        {
            return Array.Empty<TerrainGraphicReference>();
        }

        var candidates = new List<TerrainGraphicReference>();
        foreach (var candidate in References)
        {
            if (candidate == reference)
            {
                continue;
            }

            if (self.Buckets.IsComparable(_features[candidate].Buckets))
            {
                ComparisonCount++;
                candidates.Add(candidate);
            }
        }

        return candidates.AsReadOnly();
    }

    private static void ValidateFrameBounds(string path, TerrainFrame frame, int imageWidth, int imageHeight)
    {
        int right;
        int bottom;
        try
        {
            right = checked(frame.Rect.X + frame.Rect.Width);
            bottom = checked(frame.Rect.Y + frame.Rect.Height);
        }
        catch (OverflowException)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.FrameOutOfBounds,
                $"Frame {frame.Reference.Sheet}:{frame.Reference.Graphic} rect overflows Int32.",
                path,
                frame.Reference);
        }

        if (right > imageWidth || bottom > imageHeight)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.FrameOutOfBounds,
                $"Frame {frame.Reference.Sheet}:{frame.Reference.Graphic} rect {frame.Rect.X},{frame.Rect.Y},{frame.Rect.Width}x{frame.Rect.Height} exceeds image {imageWidth}x{imageHeight}.",
                path,
                frame.Reference);
        }
    }

    private sealed class ImageSharpTerrainSheetImageLoader : ITerrainSheetImageLoader
    {
        public Image<Rgba32> Load(string path)
        {
            try
            {
                return Image.Load<Rgba32>(path);
            }
            catch (FileNotFoundException ex)
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.SheetNotFound,
                    $"Sheet image not found: {path}.",
                    path,
                    innerException: ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.SheetReadFailed,
                    $"Failed to read sheet: {path}.",
                    path,
                    innerException: ex);
            }
            catch (ImageFormatException ex)
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.SheetDecodeFailed,
                    $"Failed to decode sheet: {path}.",
                    path,
                    innerException: ex);
            }
        }
    }
}
