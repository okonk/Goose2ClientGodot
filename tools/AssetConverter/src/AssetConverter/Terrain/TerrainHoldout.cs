using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Goose2.AssetConverter.Terrain;

public static class TerrainHoldout
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("terrain-holdout-v1\0");

    public static ulong DigestPrefix(string mapIdentity)
    {
        if (mapIdentity is null)
        {
            throw new ArgumentNullException(nameof(mapIdentity));
        }

        var path = Encoding.UTF8.GetBytes(mapIdentity);
        var payload = new byte[Header.Length + 4 + path.Length];
        Header.CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(Header.Length, 4), (uint)path.Length);
        path.CopyTo(payload, Header.Length + 4);

        var digest = SHA256.HashData(payload);
        return BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(0, 8));
    }

    public static bool IsHeldOut(string mapIdentity, int holdoutModulo)
    {
        if (holdoutModulo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(holdoutModulo));
        }

        return DigestPrefix(mapIdentity) % (ulong)holdoutModulo == 0;
    }
}
