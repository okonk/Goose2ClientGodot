using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MapEditor.Core;

public static class TerrainStableHash
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;
    private static readonly byte[] PatternDomain = "terrain-pattern-v1"u8.ToArray();
    private static readonly byte[] VariantDomain = "terrain-variant-v1"u8.ToArray();

    public static ulong HashPattern(Guid centerId, int x, int y, TerrainPattern desiredPeers)
        => Hash(PatternDomain, centerId, x, y, desiredPeers, null);

    public static ulong HashVariant(Guid centerId, int x, int y, TerrainPattern desiredPeers, TerrainPattern candidate)
        => Hash(VariantDomain, centerId, x, y, desiredPeers, candidate);

    private static ulong Hash(byte[] domain, Guid centerId, int x, int y, TerrainPattern desiredPeers, TerrainPattern? candidate)
    {
        var hash = OffsetBasis;
        hash = FoldLengthPrefixed(hash, domain);
        hash = FoldLengthPrefixed(hash, GuidToBytes(centerId));

        var coordinates = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(coordinates, x);
        BinaryPrimitives.WriteInt32LittleEndian(coordinates.AsSpan(4), y);
        hash = Fold(hash, coordinates);

        foreach (var peer in PeerOrder(desiredPeers))
        {
            hash = FoldPeer(hash, peer);
        }

        if (candidate is not null)
        {
            hash = FoldPeer(hash, candidate.Value.Center);
            foreach (var peer in PeerOrder(candidate.Value))
            {
                hash = FoldPeer(hash, peer);
            }
        }

        return hash;
    }

    private static Guid?[] PeerOrder(TerrainPattern pattern)
        => [pattern.North, pattern.East, pattern.South, pattern.West, pattern.NorthEast, pattern.SouthEast, pattern.SouthWest, pattern.NorthWest];

    private static ulong FoldLengthPrefixed(ulong hash, byte[] payload)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
        return Fold(Fold(hash, header), payload);
    }

    private static ulong FoldPeer(ulong hash, Guid? peer)
    {
        if (peer is null)
        {
            return Fold(hash, [0]);
        }

        var buffer = new byte[37];
        buffer[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1), 32);
        GuidToBytes(peer.Value).CopyTo(buffer.AsSpan(5));
        return Fold(hash, buffer);
    }

    private static byte[] GuidToBytes(Guid guid)
        => Encoding.UTF8.GetBytes(guid.ToString("N", CultureInfo.InvariantCulture));

    private static ulong Fold(ulong hash, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= Prime;
        }

        return hash;
    }
}
