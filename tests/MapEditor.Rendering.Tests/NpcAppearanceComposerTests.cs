using System;
using System.IO;
using System.Linq;
using MapEditor.GameData.Rows;
using MapEditor.Rendering;
using MapEditor.Rendering.Tests.Fakes;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class NpcAppearanceComposerTests
{
    private const string MapManifestJson = """
        { "tileSize": 32, "sheets": {
          "1000": {
            "108760": [0, 0, 48, 64],
            "108761": [16, 0, 32, 48],
            "108762": [0, 0, 64, 64],
            "108763": [0, 0, 47, 49],
            "108764": [0, 0, 32, 32]
          },
          "1001": { "1": [0, 0, 32, 32] }
        } }
        """;

    private const string AppearanceManifestJson = """
        { "version": 1, "parts": {
          "Body": {
            "1": { "noEquip": [1000, 108760], "equip": [1000, 108761] },
            "2": { "noEquip": [1000, 108761] },
            "11": { "noEquip": [1000, 108761] },
            "99": { "noEquip": [1000, 108761] },
            "100": { "noEquip": [1000, 108762] },
            "200": { "noEquip": [1000, 108764] },
            "201": { "noEquip": [1000, 108763] }
          },
          "Hair": { "2": { "equip": [1000, 108763] } },
          "Eyes": { "7": { "noEquip": [1000, 108764] }, "999": { "noEquip": [1001, 1] } },
          "Chest": { "5": { "equip": [1000, 108761] }, "8": { "noEquip": [1000, 108764] } },
          "Helm": { "9": { "equip": [1000, 108764] } },
          "Legs": { "3": { "noEquip": [1000, 108761] }, "4": { "noEquip": [1000, 108764] } },
          "Feet": { "4": { "noEquip": [1000, 108761] } },
          "Hand": { "6": { "equip": [1000, 108763] }, "7": { "equip": [1000, 108764] } }
        } }
        """;

    private static readonly RgbaValue NoTint = new(0, 0, 0, 0);
    private static readonly RgbaValue BodyTint = new(10, 20, 30, 40);
    private static readonly RgbaValue HairTint = new(50, 60, 70, 80);

    private static NpcAppearance Humanoid(int bodyId, string equipped, int faceId = 7, int hairId = 2, int bodyState = 3)
        => new(1, "Goose", bodyState, bodyId, BodyTint, faceId, hairId, HairTint, equipped);

    [Fact]
    public void Compose_AllReadyHumanoidEmitsLockedNineSlotOrderWithExactReferencesTintsAndRects()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(
            Humanoid(1, "5,*,9,*,3,*,4,*,6,*,7,100,110,120,130"), occurrenceIndex: 4, tileX: 2, tileY: 3);

        Assert.Equal(4, group.OccurrenceIndex);
        Assert.Equal(2, group.TileX);
        Assert.Equal(3, group.TileY);
        Assert.Equal(80, group.SortAnchorX);
        Assert.Equal(128, group.SortAnchorY);
        Assert.Null(group.EquipmentDiagnostic);

        NpcPartDrawOperation[] parts = group.Parts.ToArray();
        Assert.Equal(9, parts.Length);
        Assert.Equal(new[]
        {
            NpcPartSlot.Body, NpcPartSlot.Eyes, NpcPartSlot.Feet, NpcPartSlot.Legs, NpcPartSlot.Chest,
            NpcPartSlot.Hair, NpcPartSlot.Helm, NpcPartSlot.Shield, NpcPartSlot.Weapon
        }, parts.Select(p => p.Slot).ToArray());
        Assert.Equal(new[]
        {
            AppearancePartKind.Body, AppearancePartKind.Eyes, AppearancePartKind.Feet, AppearancePartKind.Legs,
            AppearancePartKind.Chest, AppearancePartKind.Hair, AppearancePartKind.Helm, AppearancePartKind.Hand,
            AppearancePartKind.Hand
        }, parts.Select(p => p.Kind).ToArray());
        Assert.Equal(new[] { 1, 7, 4, 3, 5, 2, 9, 6, 7 }, parts.Select(p => p.PartId).ToArray());
        Assert.Equal(new[]
        {
            new SpriteReference(1000, 108760),
            new SpriteReference(1000, 108764),
            new SpriteReference(1000, 108761),
            new SpriteReference(1000, 108761),
            new SpriteReference(1000, 108761),
            new SpriteReference(1000, 108763),
            new SpriteReference(1000, 108764),
            new SpriteReference(1000, 108763),
            new SpriteReference(1000, 108764)
        }, parts.Select(p => p.Reference).ToArray());
        Assert.Equal(new[]
        {
            BodyTint, NoTint, NoTint, NoTint, NoTint, HairTint, NoTint, NoTint, new RgbaValue(100, 110, 120, 130)
        }, parts.Select(p => p.Tint).ToArray());
        Assert.All(parts, p => Assert.True(p.IsReady));
        Assert.All(parts, p => Assert.Null(p.Diagnostic));
        Assert.Equal(new[]
        {
            new NpcPartDestination(56, 72, 48, 64),
            new NpcPartDestination(64, 96, 32, 32),
            new NpcPartDestination(64, 80, 32, 48),
            new NpcPartDestination(64, 80, 32, 48),
            new NpcPartDestination(64, 80, 32, 48),
            new NpcPartDestination(57, 80, 47, 49),
            new NpcPartDestination(64, 96, 32, 32),
            new NpcPartDestination(57, 80, 47, 49),
            new NpcPartDestination(64, 96, 32, 32)
        }, parts.Select(p => p.Destination).ToArray());
    }

    [Fact]
    public void Compose_NonIdleBodyStateResolvesEquipVariant()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(1, NpcEquipmentParser.DefaultEquippedItems, bodyState: 0), 0, 2, 3);

        NpcPartDrawOperation body = group.Parts.Single(p => p.Slot == NpcPartSlot.Body);

        Assert.Equal(new SpriteReference(1000, 108761), body.Reference);
    }

    [Fact]
    public void Compose_BodyId99IsHumanoidAndComposesEquipment()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(99, "5,*,9,*,3,*,4,*,6,*,7,*"), 0, 0, 0);

        Assert.Null(group.EquipmentDiagnostic);
        Assert.Equal(9, group.Parts.Count);
        Assert.Equal(NpcPartSlot.Weapon, group.Parts[^1].Slot);
        Assert.Equal(7, group.Parts[^1].PartId);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(999)]
    public void Compose_MonsterBodiesEmitBodyOnlyAndIgnoreEquipment(int bodyId)
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(bodyId, "5,*,9,*,3,*,4,*,6,*,7,100,110,120,130"), 0, 2, 3);

        NpcPartDrawOperation[] parts = group.Parts.ToArray();
        Assert.Single(parts);
        Assert.Equal(NpcPartSlot.Body, parts[0].Slot);
        Assert.Equal(bodyId, parts[0].PartId);
        Assert.Null(group.EquipmentDiagnostic);
    }

    [Fact]
    public void Compose_MonsterWithMalformedEquipmentIgnoresMalformedEquipmentWithoutDiagnostic()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(100, "5,*,garbage"), 0, 2, 3);

        Assert.Null(group.EquipmentDiagnostic);
        Assert.Single(group.Parts);
        Assert.Equal(NpcPartSlot.Body, group.Parts[0].Slot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Compose_NonPositiveBodyIdIsAMissingBodyPlaceholderNotACrash(int bodyId)
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(bodyId, NpcEquipmentParser.DefaultEquippedItems), 0, 2, 3);

        NpcPartDrawOperation body = group.Parts.Single(p => p.Slot == NpcPartSlot.Body);

        Assert.Equal(bodyId, body.PartId);
        Assert.Equal(SpriteResolutionStatus.Empty, body.Status);
        Assert.False(body.IsReady);
        Assert.False(string.IsNullOrWhiteSpace(body.Diagnostic));
        Assert.Equal(new NpcPartDestination(80, 128, 0, 0), body.Destination);
        Assert.Contains(group.Parts, p => p.Slot == NpcPartSlot.Eyes);
        Assert.Contains(group.Parts, p => p.Slot == NpcPartSlot.Hair);
    }

    [Fact]
    public void Compose_EmptyMaleBodyOneFallsBackToUntintedLegsThree()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(1, NpcEquipmentParser.DefaultEquippedItems), 0, 2, 3);

        NpcPartDrawOperation legs = group.Parts.Single(p => p.Slot == NpcPartSlot.Legs);

        Assert.Equal(3, legs.PartId);
        Assert.Equal(NoTint, legs.Tint);
        Assert.DoesNotContain(group.Parts, p => p.Slot == NpcPartSlot.Chest);
    }

    [Fact]
    public void Compose_EmptyFemaleBodyElevenFallsBackToUntintedLegsFourAndChestEight()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(11, NpcEquipmentParser.DefaultEquippedItems), 0, 2, 3);

        NpcPartDrawOperation legs = group.Parts.Single(p => p.Slot == NpcPartSlot.Legs);
        NpcPartDrawOperation chest = group.Parts.Single(p => p.Slot == NpcPartSlot.Chest);

        Assert.Equal(4, legs.PartId);
        Assert.Equal(NoTint, legs.Tint);
        Assert.Equal(8, chest.PartId);
        Assert.Equal(NoTint, chest.Tint);
    }

    [Fact]
    public void Compose_ExplicitEquipmentWinsOverUnderwearIncludingItsTint()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();

        NpcAppearanceGroup male = composer.Compose(Humanoid(1, "5,1,2,3,4,6,*,3,9,8,7,6,4,*,7,*,4,*"), 0, 2, 3);
        NpcPartDrawOperation legs = male.Parts.Single(p => p.Slot == NpcPartSlot.Legs);
        Assert.Equal(3, legs.PartId);
        Assert.Equal(new RgbaValue(9, 8, 7, 6), legs.Tint);

        NpcAppearanceGroup female = composer.Compose(Humanoid(11, "5,1,2,3,4,6,*,3,*,4,*,7,*,4,*"), 0, 2, 3);
        NpcPartDrawOperation chest = female.Parts.Single(p => p.Slot == NpcPartSlot.Chest);
        Assert.Equal(5, chest.PartId);
        Assert.Equal(new RgbaValue(1, 2, 3, 4), chest.Tint);
        Assert.DoesNotContain(female.Parts, p => p.Slot == NpcPartSlot.Legs && p.PartId == 4);
    }

    [Fact]
    public void Compose_BodiesOtherThanOneAndElevenGetNoUnderwearFallback()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(2, NpcEquipmentParser.DefaultEquippedItems), 0, 2, 3);

        Assert.DoesNotContain(group.Parts, p => p.Slot == NpcPartSlot.Legs);
        Assert.DoesNotContain(group.Parts, p => p.Slot == NpcPartSlot.Chest);
    }

    [Fact]
    public void Compose_DuplicateHandIdsRemainTwoIndependentlyOrderedSlotsShieldThenWeapon()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(1, "5,*,9,*,3,*,4,*,6,*,6,1,2,3,4"), 0, 2, 3);

        NpcPartDrawOperation[] hands = group.Parts.Where(p => p.Kind == AppearancePartKind.Hand).ToArray();

        Assert.Equal(2, hands.Length);
        Assert.Equal(NpcPartSlot.Shield, hands[0].Slot);
        Assert.Equal(NpcPartSlot.Weapon, hands[1].Slot);
        Assert.Equal(6, hands[0].PartId);
        Assert.Equal(6, hands[1].PartId);
        Assert.Equal(new SpriteReference(1000, 108763), hands[0].Reference);
        Assert.Equal(new SpriteReference(1000, 108763), hands[1].Reference);
        Assert.Equal(NoTint, hands[0].Tint);
        Assert.Equal(new RgbaValue(1, 2, 3, 4), hands[1].Tint);
    }

    [Fact]
    public void Compose_MalformedEquipmentYieldsOneGroupDiagnosticAndNoEquipmentSlotsWithoutShifting()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(2, "0,*,garbage"), 0, 2, 3);

        Assert.False(string.IsNullOrWhiteSpace(group.EquipmentDiagnostic));
        NpcPartDrawOperation[] parts = group.Parts.ToArray();
        Assert.Equal(new[] { NpcPartSlot.Body, NpcPartSlot.Eyes, NpcPartSlot.Hair }, parts.Select(p => p.Slot).ToArray());
        Assert.Equal(BodyTint, parts[0].Tint);
        Assert.Equal(NoTint, parts[1].Tint);
        Assert.Equal(HairTint, parts[2].Tint);
        Assert.All(parts, p => Assert.True(p.IsReady));
    }

    [Fact]
    public void Compose_MissingManifestEntryEmitsOnePlaceholderWhileReadySiblingsRemain()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(1, "5,*,9,*,3,*,4,*,6,*,7,*", hairId: 999), 0, 2, 3);

        NpcPartDrawOperation hair = group.Parts.Single(p => p.Slot == NpcPartSlot.Hair);

        Assert.Equal(999, hair.PartId);
        Assert.Equal(SpriteResolutionStatus.UnknownGraphic, hair.Status);
        Assert.False(hair.IsReady);
        Assert.False(string.IsNullOrWhiteSpace(hair.Diagnostic));
        Assert.Contains("999", hair.Diagnostic);
        Assert.Equal(new NpcPartDestination(80, 128, 0, 0), hair.Destination);
        Assert.Equal(8, group.Parts.Count(p => p.IsReady));
    }

    [Fact]
    public void Compose_MissingTextureEmitsOnePlaceholderWithCacheStatusVerbatimWhileSiblingsRemain()
    {
        using Fixture fixture = new(
            new FakeSpriteSheetLoader(path =>
                path.EndsWith("1001.png")
                    ? SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, "no such file")
                    : SpriteSheetLoadResult.Success(new FakeSpriteSheetImage(64, 64))));
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearanceGroup group = composer.Compose(Humanoid(1, "5,*,9,*,3,*,4,*,6,*,7,*", faceId: 999), 0, 2, 3);

        NpcPartDrawOperation eyes = group.Parts.Single(p => p.Slot == NpcPartSlot.Eyes);

        Assert.Equal(SpriteResolutionStatus.MissingSheetFile, eyes.Status);
        Assert.False(eyes.IsReady);
        Assert.False(string.IsNullOrWhiteSpace(eyes.Diagnostic));
        Assert.Equal(8, group.Parts.Count(p => p.IsReady));
    }

    [Fact]
    public void Compose_DestinationsMatchClientIntegerAnchorFormulaForShortStandardTallAndOddFrames()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();

        NpcPartDrawOperation short32 = composer.Compose(Humanoid(200, NpcEquipmentParser.DefaultEquippedItems), 0, 0, 0).Parts[0];
        NpcPartDrawOperation standard48 = composer.Compose(Humanoid(99, NpcEquipmentParser.DefaultEquippedItems), 0, 0, 0).Parts[0];
        NpcPartDrawOperation tall64 = composer.Compose(Humanoid(1, NpcEquipmentParser.DefaultEquippedItems), 0, 5, 10).Parts[0];
        NpcPartDrawOperation odd47x49 = composer.Compose(Humanoid(201, NpcEquipmentParser.DefaultEquippedItems), 0, 0, 0).Parts[0];

        Assert.Equal(new NpcPartDestination(0, 0, 32, 32), short32.Destination);
        Assert.Equal(new NpcPartDestination(0, -16, 32, 48), standard48.Destination);
        Assert.Equal(new NpcPartDestination(152, 296, 48, 64), tall64.Destination);
        Assert.Equal(new NpcPartDestination(-7, -16, 47, 49), odd47x49.Destination);
    }

    [Fact]
    public void Compose_DestinationsAreUnscaledAndIndependentOfTilePositionBeyondTheAnchor()
    {
        using Fixture fixture = new();
        NpcAppearanceComposer composer = fixture.CreateComposer();
        NpcAppearance npc = Humanoid(1, NpcEquipmentParser.DefaultEquippedItems);

        NpcPartDrawOperation atOrigin = composer.Compose(npc, 0, 0, 0).Parts[0];
        NpcPartDrawOperation atFarTile = composer.Compose(npc, 0, 100, 100).Parts[0];

        Assert.Equal(48, atOrigin.Destination.Width);
        Assert.Equal(64, atOrigin.Destination.Height);
        Assert.Equal(48, atFarTile.Destination.Width);
        Assert.Equal(64, atFarTile.Destination.Height);
        Assert.Equal(3192, atFarTile.Destination.X);
        Assert.Equal(3176, atFarTile.Destination.Y);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(FakeSpriteSheetLoader? loader = null)
        {
            AssetRoot = Path.Combine(Path.GetTempPath(), "npc-appearance-composer-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(AssetRoot);
            File.WriteAllText(Path.Combine(AssetRoot, "manifest.json"), MapManifestJson);
            File.WriteAllText(Path.Combine(AssetRoot, "appearance-manifest.json"), AppearanceManifestJson);
            loader ??= new FakeSpriteSheetLoader();
            Cache = new SpriteAssetCache(AssetRoot, SpriteManifest.Parse(MapManifestJson), loader);
        }

        public string AssetRoot { get; }
        public SpriteAssetCache Cache { get; }

        public NpcAppearanceComposer CreateComposer()
            => new(new AppearanceAssetCatalog(AppearanceManifest.Parse(AppearanceManifestJson), Cache));

        public void Dispose()
        {
            Cache.Dispose();
            Directory.Delete(AssetRoot, recursive: true);
        }
    }
}
