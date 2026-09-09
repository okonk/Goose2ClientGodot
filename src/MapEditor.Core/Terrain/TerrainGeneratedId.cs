using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MapEditor.Core.Terrain;

public static class TerrainGeneratedId
{
    public static string Create(TerrainTopology topology, IEnumerable<TerrainGraphicReference> members)
    {
        if (members is null)
        {
            throw new ArgumentNullException(nameof(members));
        }

        var sorted = members
            .OrderBy(member => member.Sheet)
            .ThenBy(member => member.Graphic)
            .ToList();

        if (sorted.Count == 0)
        {
            throw new ArgumentException("members must not be empty.", nameof(members));
        }

        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Sheet == sorted[i - 1].Sheet && sorted[i].Graphic == sorted[i - 1].Graphic)
            {
                throw new ArgumentException("members must not contain duplicates.", nameof(members));
            }
        }

        var (prefix, digit) = topology switch
        {
            TerrainTopology.FourWay => ("four-way", '4'),
            TerrainTopology.EightWay => ("eight-way", '8'),
            _ => throw new ArgumentOutOfRangeException(nameof(topology))
        };

        var payload = new StringBuilder(prefix.Length + 2);
        payload.Append(prefix).Append('|');
        for (var i = 0; i < sorted.Count; i++)
        {
            if (i > 0)
            {
                payload.Append(',');
            }

            payload.Append(sorted[i].Sheet).Append(':').Append(sorted[i].Graphic);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString()));
        return $"terrain-{digit}-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
