using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Schema;
using MapEditor.GameData.Validation;
using Xunit;

namespace MapEditor.GameData.Tests.Validation;

public class GameDataValidatorTests
{
    private static readonly MapDimensions CurrentMap = new(10, 8);

    private static NpcAppearance Appearance(int id) =>
        new(id, $"npc{id}", 0, 1, new RgbaValue(255, 0, 0, 255), 1, 1,
            new RgbaValue(0, 0, 0, 255), "[]");

    private static MapReference Map(int id) => new(id, $"map{id}", $"map{id}.png");

    private static GameDataValidator ValidatorWithWarpSql(string warpXSql, string warpYSql)
    {
        var json = $$"""
        {"sheets":[{"sheet":"Warptiles","table":"warptiles","columns":[
          {"name":"map_id","header":"map id","kind":"Id","sql":"SMALLINT","required":true,"pk":false,"ref":"Maps"},
          {"name":"map_x","header":"map x","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
          {"name":"map_y","header":"map y","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
          {"name":"warp_id","header":"warp to map id","kind":"Id","sql":"SMALLINT","required":true,"pk":false,"ref":"Maps"},
          {"name":"warp_x","header":"warp to x","kind":"Int","sql":"{{warpXSql}}","required":true,"pk":false},
          {"name":"warp_y","header":"warp to y","kind":"Int","sql":"{{warpYSql}}","required":true,"pk":false}
        ]}]}
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return new GameDataValidator(GameDataSchema.Load(stream));
    }

    [Fact]
    public void ValidateSpawns_WithValidRows_IsValid()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<NpcSpawnRow>
        {
            new(1, 100, 0, 0),
            new(2, 100, 9, 7)
        };
        var npcs = new Dictionary<int, NpcAppearance>
        {
            [1] = Appearance(1),
            [2] = Appearance(2)
        };

        var result = validator.ValidateSpawns(rows, npcs, CurrentMap);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSpawns_WithMissingNpc_EmitsNpcNotFound()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<NpcSpawnRow> { new(999, 100, 3, 3) };
        var npcs = new Dictionary<int, NpcAppearance> { [1] = Appearance(1) };

        var result = validator.ValidateSpawns(rows, npcs, CurrentMap);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationCodes.SpawnNpcNotFound, error.Code);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(10, 0)]
    [InlineData(0, 8)]
    public void ValidateSpawns_WithSourceCoordinateAtOrBeyondEdge_EmitsOutOfBounds(int x, int y)
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<NpcSpawnRow> { new(1, 100, x, y) };
        var npcs = new Dictionary<int, NpcAppearance> { [1] = Appearance(1) };

        var result = validator.ValidateSpawns(rows, npcs, CurrentMap);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationCodes.SpawnOutOfBounds, error.Code);
    }

    [Fact]
    public void ValidateSpawns_LeavesInputRowsUnchanged()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<NpcSpawnRow> { new(999, 100, -5, -5) };
        var before = rows.ToArray();
        var npcs = new Dictionary<int, NpcAppearance>();

        validator.ValidateSpawns(rows, npcs, CurrentMap);

        Assert.Equal(before, rows);
    }

    [Fact]
    public void ValidateSpawns_WithNonPositiveCurrentDimensions_ThrowsArgumentOutOfRange()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<NpcSpawnRow>();
        var npcs = new Dictionary<int, NpcAppearance>();

        Assert.Throws<ArgumentOutOfRangeException>(() => validator.ValidateSpawns(rows, npcs, new MapDimensions(0, 8)));
        Assert.Throws<ArgumentOutOfRangeException>(() => validator.ValidateSpawns(rows, npcs, new MapDimensions(10, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => validator.ValidateSpawns(rows, npcs, new MapDimensions(-1, 8)));
    }

    [Fact]
    public void ValidateWarps_WithValidRows_IsValid()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow>
        {
            new(100, 5, 5, 200, 3, 4)
        };
        var maps = new Dictionary<int, MapReference> { [200] = Map(200) };
        var open = new Dictionary<int, MapDimensions> { [200] = new(50, 50) };

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateWarps_WithDuplicateSourceTiles_EmitsDuplicateSourceOnLaterOccurrences()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow>
        {
            new(100, 5, 5, 200, 1, 1),
            new(100, 5, 5, 300, 2, 2),
            new(100, 5, 5, 200, 1, 1)
        };
        var maps = new Dictionary<int, MapReference>
        {
            [200] = Map(200),
            [300] = Map(300)
        };
        var open = new Dictionary<int, MapDimensions>
        {
            [200] = new(50, 50),
            [300] = new(50, 50)
        };

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        var errors = result.Errors.ToArray();
        Assert.Equal(2, errors.Length);
        Assert.All(errors, e => Assert.Equal(ValidationCodes.WarpDuplicateSource, e.Code));
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(5, -1)]
    [InlineData(10, 5)]
    [InlineData(5, 8)]
    public void ValidateWarps_WithSourceCoordinateAtOrBeyondEdge_EmitsOutOfBounds(int x, int y)
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow> { new(100, x, y, 200, 1, 1) };
        var maps = new Dictionary<int, MapReference> { [200] = Map(200) };
        var open = new Dictionary<int, MapDimensions> { [200] = new(50, 50) };

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationCodes.WarpOutOfBounds, error.Code);
    }

    [Fact]
    public void ValidateWarps_WithMissingDestinationMap_EmitsDestinationMapNotFound()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow> { new(100, 5, 5, 999, 1, 1) };
        var maps = new Dictionary<int, MapReference> { [200] = Map(200) };
        var open = new Dictionary<int, MapDimensions>();

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationCodes.WarpDestinationMapNotFound, error.Code);
    }

    [Fact]
    public void ValidateWarps_WithNegativeDestinationCoordinate_EmitsNumericRange()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow> { new(100, 5, 5, 200, -32769, 0) };
        var maps = new Dictionary<int, MapReference> { [200] = Map(200) };
        var open = new Dictionary<int, MapDimensions>();

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationCodes.WarpDestinationNumericRange, error.Code);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void ValidateWarps_WithNegativeDestinationWithinSqlRange_EmitsNumericRangeOnly(int warpX, int warpY)
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow> { new(100, 5, 5, 200, warpX, warpY) };
        var maps = new Dictionary<int, MapReference> { [200] = Map(200) };
        var open = new Dictionary<int, MapDimensions>();

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationCodes.WarpDestinationNumericRange, error.Code);
    }

    [Fact]
    public void ValidateWarps_WithDestinationAboveSmallintRange_EmitsNumericRange()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow> { new(100, 5, 5, 200, 32768, 0) };
        var maps = new Dictionary<int, MapReference> { [200] = Map(200) };
        var open = new Dictionary<int, MapDimensions>();

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationCodes.WarpDestinationNumericRange, error.Code);
    }

    [Fact]
    public void ValidateWarps_WithOpenDestinationOutOfBounds_EmitsDestinationOutOfBounds()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow> { new(100, 5, 5, 200, 10, 5) };
        var maps = new Dictionary<int, MapReference> { [200] = Map(200) };
        var open = new Dictionary<int, MapDimensions> { [200] = new(10, 10) };

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationCodes.WarpDestinationOutOfBounds, error.Code);
    }

    [Fact]
    public void ValidateWarps_WithClosedDestinationWithinSqlRange_IsValid()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow> { new(100, 5, 5, 200, 30000, 30000) };
        var maps = new Dictionary<int, MapReference> { [200] = Map(200) };
        var open = new Dictionary<int, MapDimensions>();

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateWarps_LeavesInputRowsUnchanged()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow> { new(100, -5, -5, 999, -1, -1) };
        var before = rows.ToArray();
        var maps = new Dictionary<int, MapReference>();
        var open = new Dictionary<int, MapDimensions>();

        validator.ValidateWarps(rows, maps, open, CurrentMap);

        Assert.Equal(before, rows);
    }

    [Fact]
    public void ValidateWarps_WithNonPositiveCurrentDimensions_ThrowsArgumentOutOfRange()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow>();
        var maps = new Dictionary<int, MapReference>();
        var open = new Dictionary<int, MapDimensions>();

        Assert.Throws<ArgumentOutOfRangeException>(() => validator.ValidateWarps(rows, maps, open, new MapDimensions(0, 8)));
        Assert.Throws<ArgumentOutOfRangeException>(() => validator.ValidateWarps(rows, maps, open, new MapDimensions(10, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => validator.ValidateWarps(rows, maps, open, new MapDimensions(8, -1)));
    }

    [Fact]
    public void ValidateWarps_WithNonPositiveOpenMapDimensions_ThrowsArgumentOutOfRange()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow>();
        var maps = new Dictionary<int, MapReference>();
        var open = new Dictionary<int, MapDimensions> { [200] = new MapDimensions(0, 5) };

        Assert.Throws<ArgumentOutOfRangeException>(() => validator.ValidateWarps(rows, maps, open, CurrentMap));
    }

    [Fact]
    public void ValidateWarps_EmitsIssuesInInputRowOrder()
    {
        var validator = new GameDataValidator(GameDataSchema.LoadEmbedded());
        var rows = new List<WarpRow>
        {
            new(100, -1, 5, 999, 0, 0),
            new(100, 5, 5, 200, 0, 0),
            new(100, 5, 5, 300, 0, 0)
        };
        var maps = new Dictionary<int, MapReference>
        {
            [200] = Map(200),
            [300] = Map(300)
        };
        var open = new Dictionary<int, MapDimensions> { [200] = new(50, 50) };

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        Assert.Equal(
            new[]
            {
                ValidationCodes.WarpOutOfBounds,
                ValidationCodes.WarpDestinationMapNotFound,
                ValidationCodes.WarpDuplicateSource
            },
            result.Errors.Select(e => e.Code).ToArray());
    }

    [Theory]
    [InlineData("INT")]
    [InlineData("INTEGER")]
    public void ValidateWarps_WithWiderGeneratedSql_FollowsSchemaMetadata(string sql)
    {
        var validator = ValidatorWithWarpSql(sql, sql);
        var rows = new List<WarpRow> { new(100, 5, 5, 200, 40000, 40000) };
        var maps = new Dictionary<int, MapReference> { [200] = Map(200) };
        var open = new Dictionary<int, MapDimensions>();

        var result = validator.ValidateWarps(rows, maps, open, CurrentMap);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Constructor_WithBigIntWarpColumns_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => ValidatorWithWarpSql("BIGINT", "BIGINT"));
    }

    [Fact]
    public void Constructor_WithNonIntegralWarpColumns_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => ValidatorWithWarpSql("TEXT", "SMALLINT"));
    }
}
