using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Goose2.AssetConverter.Terrain;

internal interface ITerrainMapData
{
    byte[] Bytes { get; }
    void Release();
}

internal interface ITerrainMapDataReader
{
    ITerrainMapData Open(string mapIdentity);
}

public static class TerrainCorpusLoader
{
    public const int RequiredTileSize = 32;
    public const string ManifestRelativePath = "Assets/Sprites/manifest.json";
    public const string SheetsRelativeDirectory = "Assets/Sprites/sheets";
    public const string DiagnosticCodeManifestReferenceMissing = "manifest-reference-missing";
    public const string DiagnosticCodeFrameSizeMismatch = "frame-size-mismatch";

    private static readonly byte[] FingerprintHeader = Encoding.ASCII.GetBytes("terrain-corpus-v1\0");

    public static TerrainCorpus Load(string repoRoot)
        => Load(TerrainMapInventory.Read(repoRoot), repoRoot, null);

    internal static TerrainCorpus Load(TerrainMapInventory inventory, string repoRoot, ITerrainMapDataReader? reader = null)
    {
        if (inventory.FileNames.Count == 0)
        {
            throw new TerrainGenerationException(TerrainGenerationError.NoMaps, "Map inventory lists no maps.");
        }

        var fullRoot = Path.GetFullPath(repoRoot);
        var (frameIndex, manifestBytes) = LoadManifest(fullRoot);
        var mapsDirectory = Path.Combine(fullRoot, TerrainMapInventory.MapsDirectory);
        var mapReader = reader ?? new FileTerrainMapDataReader(mapsDirectory);
        var inventoryPath = Path.Combine(fullRoot, TerrainMapInventory.RelativePath);
        var manifestPath = Path.Combine(fullRoot, ManifestRelativePath);

        // Fingerprint byte stream: header, then records 0x01 inventory, 0x02 maps in identity order,
        // 0x03 manifest, 0x04 relevant sheets in numeric order; content is reread from disk at hash
        // time so no map byte array outlives its scan.
        using var source = new FingerprintStream();
        source.AddBytes(FingerprintHeader);
        AddRecord(source, 0x01, TerrainMapInventory.RelativePath, inventoryPath, inventory.Content.Length);

        var observed = new HashSet<TerrainGraphicReference>();
        var relevantSheets = new HashSet<int>();
        var diagnostics = new List<TerrainDiagnostic>();
        var maps = new List<TerrainMapDescriptor>(inventory.FileNames.Count);
        var eligibleTotal = 0;

        foreach (var identity in inventory.MapIdentities)
        {
            var mapPath = Path.Combine(mapsDirectory, Path.GetFileName(identity));
            ITerrainMapData data;
            try
            {
                data = mapReader.Open(identity);
            }
            catch (FileNotFoundException ex)
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.MapNotFound,
                    $"Map file not found: {mapPath}.",
                    mapPath,
                    innerException: ex);
            }
            catch (IOException ex)
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.MapReadFailed,
                    $"Failed to read map: {mapPath}.",
                    mapPath,
                    innerException: ex);
            }

            try
            {
                AddRecord(source, 0x02, identity, mapPath, data.Bytes.Length);

                MapDocument document;
                try
                {
                    document = MapCodec.Decode(data.Bytes);
                }
                catch (MapFormatException ex)
                {
                    throw new TerrainGenerationException(
                        TerrainGenerationError.MapDecodeFailed,
                        $"Failed to decode map: {mapPath}.",
                        mapPath,
                        innerException: ex);
                }

                var eligible = ScanMap(document, identity, frameIndex, observed, relevantSheets, diagnostics);
                maps.Add(new TerrainMapDescriptor(identity, document.Width, document.Height, eligible));
                eligibleTotal += eligible;
            }
            finally
            {
                data.Release();
            }
        }

        if (eligibleTotal == 0)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.NoEligiblePlacements,
                "No eligible layer-0 placements found in the listed maps.");
        }

        AddRecord(source, 0x03, ManifestRelativePath, manifestPath, manifestBytes.Length);

        var relevant = relevantSheets.OrderBy(sheet => sheet).ToList();
        foreach (var sheet in relevant)
        {
            var sheetPath = Path.Combine(fullRoot, SheetsRelativeDirectory, sheet + ".png");
            ValidateSheet(sheetPath, frameIndex.GetFrames(sheet));
            AddRecord(source, 0x04, $"{SheetsRelativeDirectory}/{sheet}.png", sheetPath, new FileInfo(sheetPath).Length);
        }

        using var hash = SHA256.Create();
        var digest = hash.ComputeHash(source);
        var fingerprint = "sha256:" + Convert.ToHexString(digest).ToLowerInvariant();
        return new TerrainCorpus(fingerprint, maps, frameIndex, observed, relevant, diagnostics);
    }

    private static int ScanMap(
        MapDocument document,
        string identity,
        TerrainFrameIndex frameIndex,
        HashSet<TerrainGraphicReference> observed,
        HashSet<int> relevantSheets,
        List<TerrainDiagnostic> diagnostics)
    {
        var eligible = 0;
        var seen = new HashSet<TerrainGraphicReference>();
        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                var layer = document[x, y].GetLayer(0);
                if (layer.Graphic == 0)
                {
                    continue;
                }

                var reference = new TerrainGraphicReference(layer.Sheet, layer.Graphic);
                observed.Add(reference);

                if (frameIndex.TryGetRect(reference, out var rect))
                {
                    if (rect.Width == RequiredTileSize && rect.Height == RequiredTileSize)
                    {
                        eligible++;
                        relevantSheets.Add(reference.Sheet);
                    }
                    else if (seen.Add(reference))
                    {
                        diagnostics.Add(new TerrainDiagnostic(
                            DiagnosticCodeFrameSizeMismatch,
                            $"Map {identity} uses frame {reference.Sheet}:{reference.Graphic} sized {rect.Width}x{rect.Height}; expected {RequiredTileSize}x{RequiredTileSize}.",
                            reference: reference));
                    }
                }
                else if (seen.Add(reference))
                {
                    diagnostics.Add(new TerrainDiagnostic(
                        DiagnosticCodeManifestReferenceMissing,
                        $"Map {identity} uses frame {reference.Sheet}:{reference.Graphic} that is missing from the manifest.",
                        reference: reference));
                }
            }
        }

        return eligible;
    }

    private static (TerrainFrameIndex FrameIndex, byte[] Bytes) LoadManifest(string repoRoot)
    {
        var path = Path.Combine(repoRoot, ManifestRelativePath);
        if (!File.Exists(path))
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.ManifestNotFound,
                $"Manifest not found: {path}.",
                path);
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.ManifestReadFailed,
                $"Failed to read manifest: {path}.",
                path,
                innerException: ex);
        }

        try
        {
            return (ParseManifest(bytes, path), bytes);
        }
        catch (JsonException ex)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.ManifestMalformed,
                $"Manifest is not valid JSON: {path}.",
                path,
                innerException: ex);
        }
    }

    private static TerrainFrameIndex ParseManifest(byte[] bytes, string path)
    {
        using var document = JsonDocument.Parse(new MemoryStream(bytes));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(path, "manifest root must be a JSON object.");
        }

        int? tileSize = null;
        var tileSizeCount = 0;
        JsonElement? sheets = null;
        var sheetsCount = 0;

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "tileSize":
                    tileSizeCount++;
                    if (tileSizeCount == 1 && property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt32(out var value))
                    {
                        tileSize = value;
                    }

                    break;
                case "sheets":
                    sheetsCount++;
                    if (sheetsCount == 1 && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        sheets = property.Value;
                    }

                    break;
                default:
                    throw Malformed(path, $"unexpected manifest root property '{property.Name}'.");
            }
        }

        if (tileSizeCount != 1)
        {
            throw Malformed(path, "manifest root must contain 'tileSize' exactly once.");
        }

        if (sheetsCount != 1)
        {
            throw Malformed(path, "manifest root must contain 'sheets' exactly once.");
        }

        if (tileSize is not int size)
        {
            throw Malformed(path, "'tileSize' must be an integer.");
        }

        if (size != RequiredTileSize)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.UnsupportedTileSize,
                $"Manifest tile size must be {RequiredTileSize}, got {size}.",
                path);
        }

        if (sheets is not JsonElement sheetsElement)
        {
            throw Malformed(path, "'sheets' must be a JSON object.");
        }

        var framesBySheet = new Dictionary<int, List<TerrainFrame>>();
        foreach (var sheetProperty in sheetsElement.EnumerateObject())
        {
            if (!TryParseId(sheetProperty.Name, out var sheetId))
            {
                throw Malformed(path, $"invalid sheet id '{sheetProperty.Name}'.");
            }

            if (!framesBySheet.TryAdd(sheetId, new List<TerrainFrame>()))
            {
                throw Malformed(path, $"duplicate sheet id '{sheetProperty.Name}'.");
            }

            if (sheetProperty.Value.ValueKind != JsonValueKind.Object)
            {
                throw Malformed(path, $"sheet {sheetId} frames must be a JSON object.");
            }

            foreach (var frameProperty in sheetProperty.Value.EnumerateObject())
            {
                if (!TryParseId(frameProperty.Name, out var graphicId))
                {
                    throw Malformed(path, $"invalid graphic id '{frameProperty.Name}' in sheet {sheetId}.");
                }

                var reference = new TerrainGraphicReference(sheetId, graphicId);
                if (framesBySheet[sheetId].Any(frame => frame.Reference == reference))
                {
                    throw Malformed(path, $"duplicate graphic id '{frameProperty.Name}' in sheet {sheetId}.");
                }

                framesBySheet[sheetId].Add(
                    new TerrainFrame(reference, ParseRect(frameProperty.Value, sheetId, graphicId, path)));
            }
        }

        var allFrames = new List<TerrainFrame>();
        var published = new Dictionary<int, IReadOnlyList<TerrainFrame>>();
        foreach (var (sheetId, frames) in framesBySheet)
        {
            frames.Sort((a, b) => a.Reference.Graphic.CompareTo(b.Reference.Graphic));
            published[sheetId] = frames.AsReadOnly();
            allFrames.AddRange(frames);
        }

        allFrames.Sort((a, b) =>
        {
            var bySheet = a.Reference.Sheet.CompareTo(b.Reference.Sheet);
            return bySheet != 0 ? bySheet : a.Reference.Graphic.CompareTo(b.Reference.Graphic);
        });

        return new TerrainFrameIndex(
            RequiredTileSize,
            framesBySheet.Keys.OrderBy(id => id).ToList().AsReadOnly(),
            allFrames.AsReadOnly(),
            published);
    }

    private static TerrainFrameRect ParseRect(JsonElement element, int sheet, int graphic, string path)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 4)
        {
            throw Malformed(path, $"frame {sheet}:{graphic} must be an array [x, y, width, height] of integers.");
        }

        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (element[i].ValueKind != JsonValueKind.Number || !element[i].TryGetInt32(out values[i]))
            {
                throw Malformed(path, $"frame {sheet}:{graphic} must be an array [x, y, width, height] of integers.");
            }
        }

        var x = values[0];
        var y = values[1];
        var width = values[2];
        var height = values[3];
        if (x < 0 || y < 0 || width <= 0 || height <= 0)
        {
            throw Malformed(path, $"frame {sheet}:{graphic} rect must have nonnegative x/y and positive width/height.");
        }

        try
        {
            _ = checked(x + width);
            _ = checked(y + height);
        }
        catch (OverflowException)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.NumericOverflow,
                $"frame {sheet}:{graphic} rect overflows Int32.",
                path);
        }

        return new TerrainFrameRect(x, y, width, height);
    }

    private static void ValidateSheet(string sheetPath, IReadOnlyList<TerrainFrame> frames)
    {
        if (!File.Exists(sheetPath))
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.SheetNotFound,
                $"Sheet image not found: {sheetPath}.",
                sheetPath);
        }

        try
        {
            using var stream = new FileStream(sheetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var image = Image.Load<Rgba32>(stream);
            foreach (var frame in frames)
            {
                if (frame.Rect.X + frame.Rect.Width > image.Width
                    || frame.Rect.Y + frame.Rect.Height > image.Height)
                {
                    throw new TerrainGenerationException(
                        TerrainGenerationError.FrameOutOfBounds,
                        $"Frame {frame.Reference.Sheet}:{frame.Reference.Graphic} rect {frame.Rect.X},{frame.Rect.Y},{frame.Rect.Width}x{frame.Rect.Height} exceeds image {image.Width}x{image.Height}.",
                        sheetPath,
                        frame.Reference);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.SheetReadFailed,
                $"Failed to read sheet: {sheetPath}.",
                sheetPath,
                innerException: ex);
        }
        catch (ImageFormatException ex)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.SheetDecodeFailed,
                $"Failed to decode sheet: {sheetPath}.",
                sheetPath,
                innerException: ex);
        }
    }

    private static void AddRecord(FingerprintStream source, byte kind, string recordPath, string filePath, long contentLength)
    {
        var pathBytes = Encoding.UTF8.GetBytes(recordPath);
        Span<byte> header = stackalloc byte[5];
        header[0] = kind;
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(1, 4), (uint)pathBytes.Length);
        source.AddBytes(header);
        source.AddBytes(pathBytes);
        source.AddBytes(EncodeContentLength(contentLength));
        source.AddFile(filePath);
    }

    private static byte[] EncodeContentLength(long contentLength)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)contentLength);
        return bytes;
    }

    private sealed class FingerprintStream : Stream
    {
        private sealed class Segment
        {
            public byte[]? Inline { get; }
            public string? FilePath { get; }
            public Stream? File;
            public int Offset;

            public Segment(byte[] inline)
            {
                Inline = inline;
            }

            public Segment(string filePath)
            {
                FilePath = filePath;
            }
        }

        private readonly List<Segment> _segments = new();
        private int _segmentIndex = -1;

        public void AddBytes(ReadOnlySpan<byte> bytes) => _segments.Add(new Segment(bytes.ToArray()));

        public void AddFile(string path) => _segments.Add(new Segment(path));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var total = 0;
            while (total < count)
            {
                if (_segmentIndex >= _segments.Count)
                {
                    return total;
                }

                if (_segmentIndex < 0)
                {
                    _segmentIndex = 0;
                }

                var segment = _segments[_segmentIndex];
                if (segment.Inline is not null)
                {
                    var toCopy = Math.Min(segment.Inline.Length - segment.Offset, count - total);
                    Buffer.BlockCopy(segment.Inline, segment.Offset, buffer, offset + total, toCopy);
                    segment.Offset += toCopy;
                    total += toCopy;
                    if (segment.Offset >= segment.Inline.Length)
                    {
                        _segmentIndex++;
                    }
                }
                else
                {
                    segment.File ??= new FileStream(segment.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var read = segment.File.Read(buffer, offset + total, count - total);
                    if (read == 0)
                    {
                        segment.File.Dispose();
                        segment.File = null;
                        _segmentIndex++;
                        continue;
                    }

                    total += read;
                }
            }

            return total;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var segment in _segments)
                {
                    segment.File?.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }

    private static bool TryParseId(string name, out int value)
        => int.TryParse(name, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
           && value.ToString(CultureInfo.InvariantCulture) == name;

    private static TerrainGenerationException Malformed(string path, string detail)
        => new(TerrainGenerationError.ManifestMalformed, $"Invalid manifest: {detail}", path);

    private sealed class FileTerrainMapDataReader : ITerrainMapDataReader
    {
        private readonly string _mapsDirectory;

        public FileTerrainMapDataReader(string mapsDirectory)
        {
            _mapsDirectory = mapsDirectory;
        }

        public ITerrainMapData Open(string mapIdentity)
        {
            var path = Path.Combine(_mapsDirectory, Path.GetFileName(mapIdentity));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Map file not found.", path);
            }

            try
            {
                return new ArrayTerrainMapData(File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Failed to read map: {path}.", ex);
            }
        }
    }

    private sealed class ArrayTerrainMapData : ITerrainMapData
    {
        public byte[] Bytes { get; }

        public ArrayTerrainMapData(byte[] bytes)
        {
            Bytes = bytes;
        }

        public void Release()
        {
        }
    }
}
