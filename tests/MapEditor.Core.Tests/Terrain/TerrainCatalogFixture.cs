using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;

namespace MapEditor.Core.Tests;

public static class TerrainCatalogFixture
{
    public static readonly Guid Grass = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid Water = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public static TerrainDefinition Terrain(Guid id, string name, TerrainColor? color = null)
        => new(id, name, color);

    public static TerrainGraphicReference Ref(int sheet, int graphic)
        => new(sheet, graphic);

    public static TerrainPattern Pattern(Guid? center = null, params (TerrainPeer Peer, Guid? Value)[] peers)
    {
        Guid? north = null, east = null, south = null, west = null;
        Guid? northEast = null, southEast = null, southWest = null, northWest = null;
        foreach (var (peer, value) in peers)
        {
            switch (peer)
            {
                case TerrainPeer.North: north = value; break;
                case TerrainPeer.East: east = value; break;
                case TerrainPeer.South: south = value; break;
                case TerrainPeer.West: west = value; break;
                case TerrainPeer.NorthEast: northEast = value; break;
                case TerrainPeer.SouthEast: southEast = value; break;
                case TerrainPeer.SouthWest: southWest = value; break;
                case TerrainPeer.NorthWest: northWest = value; break;
            }
        }

        return new TerrainPattern(
            Center: center,
            North: north,
            East: east,
            South: south,
            West: west,
            NorthEast: northEast,
            SouthEast: southEast,
            SouthWest: southWest,
            NorthWest: northWest);
    }

    public static TerrainPattern AllPeers(Guid center, Guid? peerValue)
        => Pattern(center,
            (TerrainPeer.North, peerValue),
            (TerrainPeer.East, peerValue),
            (TerrainPeer.South, peerValue),
            (TerrainPeer.West, peerValue),
            (TerrainPeer.NorthEast, peerValue),
            (TerrainPeer.SouthEast, peerValue),
            (TerrainPeer.SouthWest, peerValue),
            (TerrainPeer.NorthWest, peerValue));

    public static TerrainPattern Solid(Guid center)
        => AllPeers(center, center);

    public static TerrainGraphicDefinition Graphic(int sheet, int graphic, TerrainPattern pattern)
        => new(new TerrainGraphicReference(sheet, graphic), pattern);

    public static TerrainCatalog Valid()
        => new(
            new[] { Terrain(Grass, "Grass"), Terrain(Water, "Water") },
            new[]
            {
                Graphic(0, 1, Solid(Grass)),
                Graphic(0, 2, Pattern(Grass)),
                Graphic(0, 3, Pattern(Grass, (TerrainPeer.North, Water))),
                Graphic(1, 1, Solid(Water)),
                Graphic(1, 2, Pattern(Water)),
                Graphic(1, 3, Pattern(Water, (TerrainPeer.North, Grass)))
            });

    private static readonly TerrainPeer[] Clockwise =
    {
        TerrainPeer.North, TerrainPeer.NorthEast, TerrainPeer.East, TerrainPeer.SouthEast,
        TerrainPeer.South, TerrainPeer.SouthWest, TerrainPeer.West, TerrainPeer.NorthWest
    };

    private static TerrainPeer Rotate(TerrainPeer peer, int steps)
        => Clockwise[(Array.IndexOf(Clockwise, peer) + steps) % Clockwise.Length];

    public static IEnumerable<TerrainPattern> CoveragePatterns(Guid center, Guid? other)
    {
        yield return AllPeers(center, other);

        foreach (var pattern in Rotations(center,
            (TerrainPeer.North, other), (TerrainPeer.NorthEast, other), (TerrainPeer.NorthWest, other)))
        {
            yield return pattern;
        }

        foreach (var pattern in Rotations(center,
            (TerrainPeer.North, other), (TerrainPeer.NorthEast, other), (TerrainPeer.East, other)))
        {
            yield return pattern;
        }

        foreach (var pattern in Rotations(center, (TerrainPeer.NorthEast, other)))
        {
            yield return pattern;
        }
    }

    private static IEnumerable<TerrainPattern> Rotations(Guid center, params (TerrainPeer Peer, Guid? Value)[] marked)
    {
        for (var step = 0; step < 4; step++)
        {
            var peers = new List<(TerrainPeer, Guid?)>();
            foreach (var peer in Clockwise)
            {
                Guid? value = center;
                foreach (var (markedPeer, markedValue) in marked)
                {
                    if (Rotate(markedPeer, step * 2) == peer)
                    {
                        value = markedValue;
                    }
                }
                peers.Add((peer, value));
            }
            yield return Pattern(center, peers.ToArray());
        }
    }
}
