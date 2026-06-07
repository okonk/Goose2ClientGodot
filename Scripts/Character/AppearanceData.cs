using Godot;
namespace Goose2Client.Character
{
    /// <summary>Read-only appearance snapshot for the portrait (A1). Equipment tint Color alpha is the
    /// blend factor (A==0 ⇒ no tint).</summary>
    public readonly struct AppearanceData
    {
        public readonly int BodyId; public readonly Color BodyColor;
        public readonly int HairId; public readonly Color HairColor;
        public readonly int FaceId;
        public readonly int ChestId; public readonly Color ChestColor;
        public readonly int HelmId;  public readonly Color HelmColor;
        public readonly bool IsMonster;   // BodyId >= 100
        public AppearanceData(int bodyId, Color bodyColor, int hairId, Color hairColor, int faceId,
            int chestId, Color chestColor, int helmId, Color helmColor)
        {
            BodyId = bodyId; BodyColor = bodyColor; HairId = hairId; HairColor = hairColor;
            FaceId = faceId; ChestId = chestId; ChestColor = chestColor; HelmId = helmId;
            HelmColor = helmColor; IsMonster = bodyId >= 100;
        }
    }
}
