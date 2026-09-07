using System;
using System.Collections.Generic;
using MapEditor.GameData.Rows;

namespace MapEditor.Rendering;

public sealed class NpcAppearanceComposer
{
    public const int TileSize = 32;
    public const int MonsterBodyId = 100;
    public const int UnderwearMaleBodyId = 1;
    public const int UnderwearFemaleBodyId = 11;
    public const int UnderwearMaleLegsId = 3;
    public const int UnderwearFemaleLegsId = 4;
    public const int UnderwearFemaleChestId = 8;

    private static readonly RgbaValue NoTint = new(0, 0, 0, 0);

    private readonly AppearanceAssetCatalog _catalog;

    public NpcAppearanceComposer(AppearanceAssetCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public NpcAppearanceGroup Compose(NpcAppearance appearance, int occurrenceIndex, int tileX, int tileY)
    {
        int anchorX = tileX * TileSize + 16;
        int anchorY = (tileY + 1) * TileSize;
        bool humanoid = appearance.BodyId < MonsterBodyId;

        NpcEquipment equipment = default;
        string? equipmentDiagnostic = null;

        if (humanoid && !NpcEquipmentParser.TryParse(appearance.EquippedItems, out equipment, out string? diagnostic))
        {
            equipmentDiagnostic = diagnostic;
        }

        int legsId = equipment.Legs.Id;
        RgbaValue legsTint = equipment.Legs.Tint;
        int chestId = equipment.Chest.Id;
        RgbaValue chestTint = equipment.Chest.Tint;

        if (humanoid)
        {
            if (legsId == 0)
            {
                if (appearance.BodyId == UnderwearMaleBodyId)
                {
                    legsId = UnderwearMaleLegsId;
                    legsTint = NoTint;
                }
                else if (appearance.BodyId == UnderwearFemaleBodyId)
                {
                    legsId = UnderwearFemaleLegsId;
                    legsTint = NoTint;
                }
            }

            if (chestId == 0 && appearance.BodyId == UnderwearFemaleBodyId)
            {
                chestId = UnderwearFemaleChestId;
                chestTint = NoTint;
            }
        }

        List<NpcPartDrawOperation> parts = new(9);
        parts.Add(BuildPart(NpcPartSlot.Body, AppearancePartKind.Body, appearance.BodyId, appearance.BodyTint, appearance.BodyState, anchorX, anchorY));

        if (humanoid)
        {
            AddPart(parts, NpcPartSlot.Eyes, AppearancePartKind.Eyes, appearance.FaceId, NoTint, appearance.BodyState, anchorX, anchorY);
            AddPart(parts, NpcPartSlot.Feet, AppearancePartKind.Feet, equipment.Feet.Id, equipment.Feet.Tint, appearance.BodyState, anchorX, anchorY);
            AddPart(parts, NpcPartSlot.Legs, AppearancePartKind.Legs, legsId, legsTint, appearance.BodyState, anchorX, anchorY);
            AddPart(parts, NpcPartSlot.Chest, AppearancePartKind.Chest, chestId, chestTint, appearance.BodyState, anchorX, anchorY);
            AddPart(parts, NpcPartSlot.Hair, AppearancePartKind.Hair, appearance.HairId, appearance.HairTint, appearance.BodyState, anchorX, anchorY);
            AddPart(parts, NpcPartSlot.Helm, AppearancePartKind.Helm, equipment.Helm.Id, equipment.Helm.Tint, appearance.BodyState, anchorX, anchorY);
            AddPart(parts, NpcPartSlot.Shield, AppearancePartKind.Hand, equipment.Shield.Id, equipment.Shield.Tint, appearance.BodyState, anchorX, anchorY);
            AddPart(parts, NpcPartSlot.Weapon, AppearancePartKind.Hand, equipment.Weapon.Id, equipment.Weapon.Tint, appearance.BodyState, anchorX, anchorY);
        }

        return new NpcAppearanceGroup(occurrenceIndex, tileX, tileY, anchorX, anchorY, parts.AsReadOnly(), equipmentDiagnostic);
    }

    private void AddPart(List<NpcPartDrawOperation> parts, NpcPartSlot slot, AppearancePartKind kind, int id, RgbaValue tint, int bodyState, int anchorX, int anchorY)
    {
        if (id > 0)
        {
            parts.Add(BuildPart(slot, kind, id, tint, bodyState, anchorX, anchorY));
        }
    }

    private NpcPartDrawOperation BuildPart(NpcPartSlot slot, AppearancePartKind kind, int id, RgbaValue tint, int bodyState, int anchorX, int anchorY)
    {
        if (id <= 0)
        {
            return new NpcPartDrawOperation(
                slot,
                kind,
                id,
                SpriteResolutionStatus.Empty,
                default,
                default,
                $"Body id {id} is not a positive graphic id.",
                tint,
                new NpcPartDestination(anchorX, anchorY, 0, 0));
        }

        _catalog.TryResolve(kind, id, bodyState, out SpriteResolution resolution);

        NpcPartDestination destination = resolution.Status == SpriteResolutionStatus.Ready
            ? ComputeDestination(anchorX, anchorY, resolution.SourceRect.Width, resolution.SourceRect.Height)
            : new NpcPartDestination(anchorX, anchorY, 0, 0);

        return new NpcPartDrawOperation(
            slot,
            kind,
            id,
            resolution.Status,
            resolution.Reference,
            resolution.SourceRect,
            resolution.Diagnostic,
            tint,
            destination);
    }

    // Mirrors the live client's integer center anchor (Scripts/Character/CharacterAnchor.cs);
    // destination is unscaled tile-space, viewport scaling happens later.
    private static NpcPartDestination ComputeDestination(int anchorX, int anchorY, int width, int height)
    {
        int offsetY = Math.Max((height - 48) / 2, 0) - height / 2;
        return new NpcPartDestination(anchorX - width / 2, anchorY + offsetY - height / 2, width, height);
    }
}
