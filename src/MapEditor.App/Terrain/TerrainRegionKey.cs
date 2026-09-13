using MapEditor.Core;

namespace MapEditor.App.Terrain;

internal readonly record struct TerrainRegionKey(
    TerrainGraphicReference Graphic,
    TerrainPeer Peer);
