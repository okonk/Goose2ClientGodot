namespace MapEditor.Core;

internal readonly record struct MapLayerChange(int X, int Y, int LayerIndex, MapTileLayer Before, MapTileLayer After);

internal readonly record struct MapFlagsChange(int X, int Y, int BeforeFlags, int AfterFlags);

internal readonly record struct MapTileSnapshot(int X, int Y, MapTile Tile);
