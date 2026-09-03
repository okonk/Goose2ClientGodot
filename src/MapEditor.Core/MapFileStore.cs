using System;
using System.IO;
using System.Security.Cryptography;

namespace MapEditor.Core;

public sealed class MapFileStore
{
    private const string TempPrefix = ".map-";
    private const string TempSuffix = ".tmp";

    private readonly Action<string, string> _replace;

    public MapFileStore() : this(DefaultReplace)
    {
    }

    internal MapFileStore(Action<string, string> replace)
    {
        _replace = replace;
    }

    public OpenedMap Open(string path)
    {
        var full = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(full);
        var document = MapCodec.Decode(bytes);
        return new OpenedMap(document, new MapFileRevision(Hash(bytes)));
    }

    public MapFileRevision Save(string path, MapDocument document, MapFileRevision? expectedRevision = null)
    {
        var full = Path.GetFullPath(path);
        var bytes = MapCodec.Encode(document);
        var directory = Path.GetDirectoryName(full)!;
        var temp = Path.Combine(directory, TempPrefix + Guid.NewGuid().ToString("N") + TempSuffix);

        var failed = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                // Best available file-data persistence request before replacement; not directory-metadata or crash durability.
                stream.Flush(flushToDisk: true);
            }

            if (expectedRevision is { } expected)
            {
                var actual = File.Exists(full)
                    ? (MapFileRevision?)new MapFileRevision(Hash(File.ReadAllBytes(full)))
                    : null;
                if (actual != expected)
                {
                    throw new MapExternalChangeException(full, expected, actual);
                }
            }

            // Single same-filesystem rename/replace; no copy/delete fallback, which would open a partial-file window.
            _replace(temp, full);
            return new MapFileRevision(Hash(bytes));
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            // Never remove or rewrite the destination as cleanup; on the success path a cleanup failure is surfaced.
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch when (failed)
            {
            }
        }
    }

    private static void DefaultReplace(string temp, string destination)
    {
        File.Move(temp, destination, overwrite: true);
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
