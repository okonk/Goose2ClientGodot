namespace MapEditor.GameData.Rows;

public readonly record struct MapReference(int MapId, string MapName, string MapFilename);

public readonly record struct RgbaValue(int R, int G, int B, int A);

public readonly record struct NpcAppearance(
    int NpcId,
    string NpcName,
    int BodyState,
    int BodyId,
    RgbaValue BodyTint,
    int FaceId,
    int HairId,
    RgbaValue HairTint,
    string EquippedItems);

public readonly record struct NpcSpawnRow(int NpcId, int MapId, int MapX, int MapY,
                                          string Properties = "");

public readonly record struct WarpRow(int MapId, int MapX, int MapY, int WarpId, int WarpX, int WarpY);

public readonly record struct MapDimensions(int Width, int Height);
