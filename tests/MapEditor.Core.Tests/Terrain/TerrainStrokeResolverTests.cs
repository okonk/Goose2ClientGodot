using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.Core.Tests.Terrain;

public class TerrainStrokeResolverTests
{
    private const int Layer = 0;

    private static TerrainSetDefinition CreateSet(TerrainTopology topology, int sheet, params int[] missingMasks)
    {
        var masks = TerrainMasks.Required(topology)
            .Where(mask => !missingMasks.Contains(mask))
            .Select(mask => new TerrainMaskDefinition(mask, [new TerrainGraphicReference(sheet, mask + 1)]))
            .ToList();
        var members = masks
            .Select(mask => TerrainCatalogFixture.CreateMember(sheet, mask.Mask + 1))
            .ToArray();
        return TerrainCatalogFixture.CreateSet(topology, members, masks: masks);
    }

    private static TerrainRuntimeSet GetSet(TerrainMapResolver resolver, string id)
    {
        Assert.True(resolver.TryGetEnabledTerrain(id, out var set));
        return set!;
    }

    private static MapDocument CreateDocument(int width, int height, params (int X, int Y, int Sheet, int Graphic)[] cells)
    {
        var document = MapDocument.Create(width, height);
        foreach (var (x, y, sheet, graphic) in cells)
        {
            document.SetLayer(x, y, Layer, new MapTileLayer(sheet, graphic));
        }

        return document;
    }

    private static TerrainStrokeResolveRequest CreateRequest(
        MapDocument document,
        TerrainMapResolver resolver,
        TerrainRuntimeSet selected,
        TerrainEditMode mode,
        params int[] cells) =>
        CreateRequest(
            document,
            resolver,
            selected,
            mode,
            new TerrainCumulativeIntent(),
            new TerrainDisplacedOwners(),
            cells);

    private static TerrainStrokeResolveRequest CreateRequest(
        MapDocument document,
        TerrainMapResolver resolver,
        TerrainRuntimeSet selected,
        TerrainEditMode mode,
        TerrainCumulativeIntent cumulative,
        TerrainDisplacedOwners displaced,
        int[] cells) =>
        new(
            document,
            Layer,
            resolver,
            selected,
            mode,
            cumulative,
            displaced,
            cells);

    private static int Index(int width, int x, int y) => y * width + x;

    private static void AssertPatch(TerrainStrokeResolution resolution, params (int CellIndex, MapTileLayer Target)[] expected)
    {
        Assert.True(resolution.Succeeded);
        Assert.Null(resolution.Failure);
        Assert.NotNull(resolution.Patch);
        Assert.Equal(
            expected,
            resolution.Patch!.Cells.Select(cell => (cell.CellIndex, cell.Target)).ToArray());
    }

    private static void AssertNoPatch(TerrainStrokeResolution resolution)
    {
        Assert.False(resolution.Succeeded);
        Assert.Null(resolution.Patch);
        Assert.Empty(resolution.IntentAdditions);
        Assert.Empty(resolution.DisplacedOwnerAdditions);
        Assert.NotNull(resolution.Failure);
    }

    private static void ApplyPatch(MapDocument document, TerrainStrokeResolution resolution)
    {
        foreach (var cell in resolution.Patch!.Cells)
        {
            document.SetLayer(cell.CellIndex % document.Width, cell.CellIndex / document.Width, Layer, cell.Target);
        }
    }

    private static void AssertGrid(MapDocument document, params (int X, int Y, MapTileLayer Layer)[] cells)
    {
        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                var expected = cells.FirstOrDefault(cell => cell.X == x && cell.Y == y).Layer;
                Assert.Equal(expected, document.GetTile(y * document.Width + x).GetLayer(Layer));
            }
        }
    }

    [Fact]
    public void ResolvePaint_SelectedFourWayCellAndNeighborsUseFinalIntent()
    {
        var setDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setDefinition));
        var a = GetSet(resolver, setDefinition.Id);

        var document = CreateDocument(3, 3, (0, 1, 1, 1));
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, Index(3, 1, 1)));

        AssertPatch(resolution,
            (Index(3, 0, 1), new MapTileLayer(1, 3)),
            (Index(3, 1, 1), new MapTileLayer(1, 9)));
        Assert.Equal([new TerrainCellIntent(Index(3, 1, 1), a)], resolution.IntentAdditions);
        Assert.Empty(resolution.DisplacedOwnerAdditions);
        Assert.Equal(5, resolution.InspectedCellCount);

        ApplyPatch(document, resolution);
        AssertGrid(document,
            (0, 1, new MapTileLayer(1, 3)),
            (1, 1, new MapTileLayer(1, 9)));
    }

    [Fact]
    public void ResolvePaint_EightWayCanonicalizesSupportedDiagonals()
    {
        var setDefinition = CreateSet(TerrainTopology.EightWay, 1);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setDefinition));
        var a = GetSet(resolver, setDefinition.Id);

        var document = CreateDocument(3, 3, (0, 0, 1, 1), (1, 0, 1, 1), (0, 1, 1, 1));
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, Index(3, 1, 1)));

        AssertPatch(resolution,
            (Index(3, 0, 0), new MapTileLayer(1, 39)),
            (Index(3, 1, 0), new MapTileLayer(1, 77)),
            (Index(3, 0, 1), new MapTileLayer(1, 20)),
            (Index(3, 1, 1), new MapTileLayer(1, 138)));
        Assert.Equal(9, resolution.InspectedCellCount);

        ApplyPatch(document, resolution);
        AssertGrid(document,
            (0, 0, new MapTileLayer(1, 39)),
            (0, 1, new MapTileLayer(1, 20)),
            (1, 0, new MapTileLayer(1, 77)),
            (1, 1, new MapTileLayer(1, 138)));
    }

    [Fact]
    public void ResolvePaint_DisplacedEightWayDiagonalUsesOwnerStencilUnion()
    {
        var aDefinition = CreateSet(TerrainTopology.EightWay, 1);
        var bDefinition = CreateSet(TerrainTopology.EightWay, 2);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(aDefinition, bDefinition));
        var a = GetSet(resolver, aDefinition.Id);
        var b = GetSet(resolver, bDefinition.Id);

        var document = CreateDocument(3, 3,
            (1, 0, 2, 39),
            (1, 1, 2, 20),
            (2, 0, 2, 77),
            (2, 1, 2, 138));
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, Index(3, 2, 0)));

        AssertPatch(resolution,
            (Index(3, 1, 0), new MapTileLayer(2, 5)),
            (Index(3, 2, 0), new MapTileLayer(1, 1)),
            (Index(3, 1, 1), new MapTileLayer(2, 4)),
            (Index(3, 2, 1), new MapTileLayer(2, 9)));
        Assert.Equal([new TerrainCellIntent(Index(3, 2, 0), a)], resolution.IntentAdditions);
        Assert.Equal([new TerrainDisplacedOwner(Index(3, 2, 0), b)], resolution.DisplacedOwnerAdditions);
        Assert.Equal(8, resolution.InspectedCellCount);

        ApplyPatch(document, resolution);
        AssertGrid(document,
            (1, 0, new MapTileLayer(2, 5)),
            (1, 1, new MapTileLayer(2, 4)),
            (2, 0, new MapTileLayer(1, 1)),
            (2, 1, new MapTileLayer(2, 9)));
    }

    [Fact]
    public void ResolveErase_SelectedWritesCanonicalEmptyAndRepairsOnlySelected()
    {
        var setDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setDefinition));
        var a = GetSet(resolver, setDefinition.Id);

        var document = CreateDocument(3, 3, (0, 1, 1, 3), (1, 0, 1, 5), (1, 1, 1, 10));
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Erase, Index(3, 1, 1)));

        AssertPatch(resolution,
            (Index(3, 1, 0), new MapTileLayer(1, 1)),
            (Index(3, 0, 1), new MapTileLayer(1, 1)),
            (Index(3, 1, 1), new MapTileLayer(0, 0)));
        Assert.Equal([new TerrainCellIntent(Index(3, 1, 1), null)], resolution.IntentAdditions);
        Assert.Empty(resolution.DisplacedOwnerAdditions);
        Assert.Equal(5, resolution.InspectedCellCount);

        ApplyPatch(document, resolution);
        AssertGrid(document,
            (0, 1, new MapTileLayer(1, 1)),
            (1, 0, new MapTileLayer(1, 1)),
            (1, 1, new MapTileLayer(0, 0)));
    }

    [Fact]
    public void ResolveErase_AdjacentOtherTerrainRemainsExactRawValue()
    {
        var aDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var bDefinition = CreateSet(TerrainTopology.FourWay, 2);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(aDefinition, bDefinition));
        var a = GetSet(resolver, aDefinition.Id);

        var document = CreateDocument(3, 3, (1, 1, 1, 1), (2, 1, 2, 1));
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Erase, Index(3, 1, 1)));

        AssertPatch(resolution, (Index(3, 1, 1), new MapTileLayer(0, 0)));
        Assert.Equal(5, resolution.InspectedCellCount);

        ApplyPatch(document, resolution);
        AssertGrid(document,
            (1, 1, new MapTileLayer(0, 0)),
            (2, 1, new MapTileLayer(2, 1)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 42)]
    [InlineData(3, 0)]
    public void ResolveErase_UnownedCanonicalEmptyOrGraphicZeroDoesNotRecanonicalizeAdjacentSelected(int sheet, int graphic)
    {
        var setDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setDefinition));
        var a = GetSet(resolver, setDefinition.Id);

        var document = CreateDocument(3, 3, (0, 0, 1, 5), (0, 1, 1, 1), (1, 1, sheet, graphic));
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Erase, Index(3, 1, 1)));

        Assert.True(resolution.Succeeded);
        Assert.Empty(resolution.Patch!.Cells);
        Assert.Empty(resolution.IntentAdditions);
        Assert.Empty(resolution.DisplacedOwnerAdditions);
        Assert.Equal(0, resolution.InspectedCellCount);

        AssertGrid(document,
            (0, 0, new MapTileLayer(1, 5)),
            (0, 1, new MapTileLayer(1, 1)),
            (1, 1, new MapTileLayer(sheet, graphic)));
    }

    [Fact]
    public void ResolveErase_CellOwnedByOtherTerrainIsExactNoOp()
    {
        var aDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var bDefinition = CreateSet(TerrainTopology.FourWay, 2);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(aDefinition, bDefinition));
        var a = GetSet(resolver, aDefinition.Id);

        var document = CreateDocument(3, 3, (1, 1, 2, 1));
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Erase, Index(3, 1, 1)));

        Assert.True(resolution.Succeeded);
        Assert.Empty(resolution.Patch!.Cells);
        Assert.Empty(resolution.IntentAdditions);
        Assert.Empty(resolution.DisplacedOwnerAdditions);
        Assert.Equal(0, resolution.InspectedCellCount);

        AssertGrid(document, (1, 1, new MapTileLayer(2, 1)));
    }

    [Fact]
    public void ResolvePaint_RawUnownedGraphicIsOverwrittenWithoutDisplacedOwner()
    {
        var setDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setDefinition));
        var a = GetSet(resolver, setDefinition.Id);

        var document = CreateDocument(3, 3, (1, 1, 7, 42));
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, Index(3, 1, 1)));

        AssertPatch(resolution, (Index(3, 1, 1), new MapTileLayer(1, 1)));
        Assert.Equal([new TerrainCellIntent(Index(3, 1, 1), a)], resolution.IntentAdditions);
        Assert.Empty(resolution.DisplacedOwnerAdditions);
        Assert.Equal(5, resolution.InspectedCellCount);

        ApplyPatch(document, resolution);
        AssertGrid(document, (1, 1, new MapTileLayer(1, 1)));
    }

    [Fact]
    public void ResolvePaint_AOverBAndBOverAAreSymmetricMemberVsNonmember()
    {
        var aDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var bDefinition = CreateSet(TerrainTopology.FourWay, 2);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(aDefinition, bDefinition));
        var a = GetSet(resolver, aDefinition.Id);
        var b = GetSet(resolver, bDefinition.Id);

        var documentA = CreateDocument(3, 3, (1, 1, 2, 1));
        var overB = TerrainStrokeResolver.Resolve(CreateRequest(documentA, resolver, a, TerrainEditMode.Paint, Index(3, 1, 1)));
        AssertPatch(overB, (Index(3, 1, 1), new MapTileLayer(1, 1)));
        Assert.Equal([new TerrainDisplacedOwner(Index(3, 1, 1), b)], overB.DisplacedOwnerAdditions);
        Assert.Equal(10, overB.InspectedCellCount);

        var documentB = CreateDocument(3, 3, (1, 1, 1, 1));
        var overA = TerrainStrokeResolver.Resolve(CreateRequest(documentB, resolver, b, TerrainEditMode.Paint, Index(3, 1, 1)));
        AssertPatch(overA, (Index(3, 1, 1), new MapTileLayer(2, 1)));
        Assert.Equal([new TerrainDisplacedOwner(Index(3, 1, 1), a)], overA.DisplacedOwnerAdditions);
        Assert.Equal(10, overA.InspectedCellCount);
    }

    [Fact]
    public void ResolvePaint_ExistingSelectedMemberStillCanonicalizesSelectedLocalRegion()
    {
        var setDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setDefinition));
        var a = GetSet(resolver, setDefinition.Id);

        var document = CreateDocument(3, 3, (0, 1, 1, 1), (1, 1, 1, 1));
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, Index(3, 1, 1)));

        AssertPatch(resolution,
            (Index(3, 0, 1), new MapTileLayer(1, 3)),
            (Index(3, 1, 1), new MapTileLayer(1, 9)));
        Assert.Equal([new TerrainCellIntent(Index(3, 1, 1), a)], resolution.IntentAdditions);
        Assert.Empty(resolution.DisplacedOwnerAdditions);
        Assert.Equal(5, resolution.InspectedCellCount);

        ApplyPatch(document, resolution);
        AssertGrid(document,
            (0, 1, new MapTileLayer(1, 3)),
            (1, 1, new MapTileLayer(1, 9)));
    }

    [Fact]
    public void Resolve_OneByOneMapUsesMaskZeroAndClipsAllNeighbors()
    {
        var setDefinition = CreateSet(TerrainTopology.EightWay, 1);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setDefinition));
        var a = GetSet(resolver, setDefinition.Id);

        var document = CreateDocument(1, 1);
        var paint = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, 0));
        AssertPatch(paint, (0, new MapTileLayer(1, 1)));
        Assert.Equal(1, paint.InspectedCellCount);
        ApplyPatch(document, paint);

        var erase = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Erase, 0));
        AssertPatch(erase, (0, new MapTileLayer(0, 0)));
        Assert.Equal(1, erase.InspectedCellCount);
        ApplyPatch(document, erase);

        AssertGrid(document);
    }

    [Fact]
    public void Resolve_MapEdgesClipOwnerStencilsAndTreatOutsideAbsent()
    {
        var setDefinition = CreateSet(TerrainTopology.EightWay, 1);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setDefinition));
        var a = GetSet(resolver, setDefinition.Id);

        var document = CreateDocument(2, 2);
        var paint = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, Index(2, 0, 0)));
        AssertPatch(paint, (Index(2, 0, 0), new MapTileLayer(1, 1)));
        Assert.Equal(4, paint.InspectedCellCount);
        ApplyPatch(document, paint);

        var paintCorner = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, Index(2, 1, 1)));
        AssertPatch(paintCorner, (Index(2, 1, 1), new MapTileLayer(1, 1)));
        Assert.Equal(4, paintCorner.InspectedCellCount);
        ApplyPatch(document, paintCorner);

        var erase = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Erase, Index(2, 1, 1)));
        AssertPatch(erase, (Index(2, 1, 1), new MapTileLayer(0, 0)));
        Assert.Equal(4, erase.InspectedCellCount);
        ApplyPatch(document, erase);

        AssertGrid(document, (0, 0, new MapTileLayer(1, 1)));
    }

    [Fact]
    public void Resolve_MultipleNewCellsUseFinalCombinedIntentIndependentOfInputOrder()
    {
        var setDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setDefinition));
        var a = GetSet(resolver, setDefinition.Id);

        var document = CreateDocument(3, 3);
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, Index(3, 1, 1), Index(3, 2, 1)));

        AssertPatch(resolution,
            (Index(3, 1, 1), new MapTileLayer(1, 3)),
            (Index(3, 2, 1), new MapTileLayer(1, 9)));
        Assert.Equal(
            [
                new TerrainCellIntent(Index(3, 1, 1), a),
                new TerrainCellIntent(Index(3, 2, 1), a)
            ],
            resolution.IntentAdditions);
        Assert.Equal(7, resolution.InspectedCellCount);

        ApplyPatch(document, resolution);
        AssertGrid(document,
            (1, 1, new MapTileLayer(1, 3)),
            (2, 1, new MapTileLayer(1, 9)));
    }

    [Fact]
    public void Resolve_CumulativeIntentAndFirstDisplacedOwnerPersistAcrossCalls()
    {
        var aDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var bDefinition = CreateSet(TerrainTopology.FourWay, 2);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(aDefinition, bDefinition));
        var a = GetSet(resolver, aDefinition.Id);
        var b = GetSet(resolver, bDefinition.Id);

        var cumulative = new TerrainCumulativeIntent();
        var displaced = new TerrainDisplacedOwners();

        var document = CreateDocument(3, 3, (1, 1, 2, 1));
        var first = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, cumulative, displaced, [Index(3, 1, 1)]));
        AssertPatch(first, (Index(3, 1, 1), new MapTileLayer(1, 1)));
        Assert.Equal(10, first.InspectedCellCount);

        cumulative.Publish(first.IntentAdditions);
        displaced.Publish(first.DisplacedOwnerAdditions);

        Assert.True(cumulative.TryGetOwner(Index(3, 1, 1), out var owned));
        Assert.Same(a, owned);
        Assert.True(displaced.TryGetFirst(Index(3, 1, 1), out var firstOwner));
        Assert.Same(b, firstOwner);

        displaced.Publish([new TerrainDisplacedOwner(Index(3, 1, 1), a)]);
        Assert.True(displaced.TryGetFirst(Index(3, 1, 1), out var retained));
        Assert.Same(b, retained);

        var second = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, cumulative, displaced, [Index(3, 2, 1)]));
        AssertPatch(second,
            (Index(3, 1, 1), new MapTileLayer(1, 3)),
            (Index(3, 2, 1), new MapTileLayer(1, 9)));
        Assert.Equal(4, second.InspectedCellCount);
    }

    [Fact]
    public void Resolve_MissingSelectedOrDisplacedMaskReturnsNoPatchOrAdditions()
    {
        var aMissing = CreateSet(TerrainTopology.FourWay, 1, 8);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(aMissing));
        var a = GetSet(resolver, aMissing.Id);

        var document = CreateDocument(3, 3, (1, 1, 1, 2));
        var bytesBefore = MapCodec.Encode(document);
        var selected = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, Index(3, 2, 1)));
        AssertNoPatch(selected);
        Assert.Equal(new TerrainEditFailure(a.Id, 8), selected.Failure);
        Assert.Equal(4, selected.InspectedCellCount);
        // Local contract proof: this resolver is mutation-free (session rollback is a separate concern).
        Assert.Equal(bytesBefore, MapCodec.Encode(document));

        var aFull = CreateSet(TerrainTopology.FourWay, 1);
        var bMissing = CreateSet(TerrainTopology.FourWay, 2, 0);
        var displacedResolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(aFull, bMissing));
        var fullA = GetSet(displacedResolver, aFull.Id);
        var missingB = GetSet(displacedResolver, bMissing.Id);

        var displacedDocument = CreateDocument(3, 3, (1, 1, 2, 2), (2, 1, 2, 2));
        var displacedBytes = MapCodec.Encode(displacedDocument);
        var displaced = TerrainStrokeResolver.Resolve(CreateRequest(displacedDocument, displacedResolver, fullA, TerrainEditMode.Paint, Index(3, 1, 1)));
        AssertNoPatch(displaced);
        Assert.Equal(new TerrainEditFailure(missingB.Id, 0), displaced.Failure);
        Assert.Equal(10, displaced.InspectedCellCount);
        Assert.Equal(displacedBytes, MapCodec.Encode(displacedDocument));
    }

    [Fact]
    public void Resolve_UnchangedOwnershipOutsideLocalStencilsIsNotInspectedOrRewritten()
    {
        var setDefinition = CreateSet(TerrainTopology.FourWay, 1);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setDefinition));
        var a = GetSet(resolver, setDefinition.Id);

        var document = CreateDocument(5, 5, (4, 3, 1, 3), (4, 4, 1, 1));
        var resolution = TerrainStrokeResolver.Resolve(CreateRequest(document, resolver, a, TerrainEditMode.Paint, Index(5, 0, 0)));

        AssertPatch(resolution, (Index(5, 0, 0), new MapTileLayer(1, 1)));
        Assert.Equal(3, resolution.InspectedCellCount);

        ApplyPatch(document, resolution);
        AssertGrid(document,
            (0, 0, new MapTileLayer(1, 1)),
            (4, 3, new MapTileLayer(1, 3)),
            (4, 4, new MapTileLayer(1, 1)));
    }
}
