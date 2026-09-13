using System;
using System.Security.Cryptography;

namespace MapEditor.Rendering;

public readonly record struct TerrainFileRevision(bool Exists, string ContentHash)
{
    public static readonly TerrainFileRevision Missing = new(false, string.Empty);

    public static TerrainFileRevision FromBytes(byte[] bytes)
        => new(true, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
}
