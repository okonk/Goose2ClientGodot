using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Schema;
using Xunit;

namespace MapEditor.GameData.Tests.Replacement;

public class ReplacementPlannerTests
{
    [Fact]
    public void PlanSpawn_ReorderedNoOp_YieldsEmptyOperations()
    {
        var planner = Planner();
        var remote = new[]
        {
            new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(1, 5, 10, 11)),
            new RemoteRow<NpcSpawnRow>(3, new NpcSpawnRow(2, 5, 20, 21)),
            new RemoteRow<NpcSpawnRow>(4, new NpcSpawnRow(3, 5, 30, 31))
        };
        var desired = new[]
        {
            new NpcSpawnRow(3, 5, 30, 31),
            new NpcSpawnRow(1, 5, 10, 11),
            new NpcSpawnRow(2, 5, 20, 21)
        };

        var plan = planner.PlanSpawnReplacement(remote, desired, 5);

        Assert.Equal("NPC Spawns", plan.Sheet);
        Assert.Empty(plan.Deletes);
        Assert.Empty(plan.Inserts);
    }

    [Fact]
    public void PlanSpawn_OneChangedRow_YieldsDeleteAndInsert()
    {
        var planner = Planner();
        var remote = new[]
        {
            new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(1, 5, 10, 11)),
            new RemoteRow<NpcSpawnRow>(3, new NpcSpawnRow(2, 5, 20, 21))
        };
        var desired = new[]
        {
            new NpcSpawnRow(1, 5, 99, 99),
            new NpcSpawnRow(2, 5, 20, 21)
        };

        var plan = planner.PlanSpawnReplacement(remote, desired, 5);

        Assert.Equal(new[] { new RowDelete(2) }, plan.Deletes);
        Assert.Single(plan.Inserts);
        Assert.Equal(new[] { "1", "5", "100", "100" }, plan.Inserts[0].CellValues);
    }

    [Fact]
    public void PlanSpawn_DuplicateCountIncrease_InsertsOnly()
    {
        var planner = Planner();
        var remote = new[] { new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(1, 5, 10, 11)) };
        var desired = new[]
        {
            new NpcSpawnRow(1, 5, 10, 11),
            new NpcSpawnRow(1, 5, 10, 11)
        };

        var plan = planner.PlanSpawnReplacement(remote, desired, 5);

        Assert.Empty(plan.Deletes);
        Assert.Equal(
            new[] { new[] { "1", "5", "11", "12" } },
            plan.Inserts.Select(i => i.CellValues));
    }

    [Fact]
    public void PlanSpawn_DuplicateCountDecrease_DeletesExactlyOneOccurrence()
    {
        var planner = Planner();
        var remote = new[]
        {
            new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(1, 5, 10, 11)),
            new RemoteRow<NpcSpawnRow>(7, new NpcSpawnRow(1, 5, 10, 11))
        };
        var desired = new[] { new NpcSpawnRow(1, 5, 10, 11) };

        var plan = planner.PlanSpawnReplacement(remote, desired, 5);

        Assert.Equal(new[] { new RowDelete(7) }, plan.Deletes);
        Assert.Empty(plan.Inserts);
    }

    [Fact]
    public void PlanSpawn_InterleavedOtherMapRows_AreNeverDeleted()
    {
        var planner = Planner();
        var remote = new[]
        {
            new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(1, 5, 10, 11)),
            new RemoteRow<NpcSpawnRow>(3, new NpcSpawnRow(9, 9, 1, 2)),
            new RemoteRow<NpcSpawnRow>(4, new NpcSpawnRow(2, 5, 20, 21)),
            new RemoteRow<NpcSpawnRow>(5, new NpcSpawnRow(9, 9, 3, 4))
        };
        var desired = new[] { new NpcSpawnRow(1, 5, 10, 11) };

        var plan = planner.PlanSpawnReplacement(remote, desired, 5);

        Assert.Equal(new[] { new RowDelete(4) }, plan.Deletes);
        Assert.Empty(plan.Inserts);
    }

    [Fact]
    public void PlanSpawn_OtherMapDesiredRows_AreNeverInserted()
    {
        var planner = Planner();
        var remote = new[] { new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(1, 5, 10, 11)) };
        var desired = new[]
        {
            new NpcSpawnRow(9, 9, 1, 2),
            new NpcSpawnRow(1, 5, 10, 11),
            new NpcSpawnRow(2, 5, 20, 21)
        };

        var plan = planner.PlanSpawnReplacement(remote, desired, 5);

        Assert.Empty(plan.Deletes);
        Assert.Equal(new[] { new[] { "2", "5", "21", "22" } }, plan.Inserts.Select(i => i.CellValues));
    }

    [Fact]
    public void PlanSpawn_MultipleUnmatchedDeletes_AreDescending()
    {
        var planner = Planner();
        var remote = new[]
        {
            new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(1, 5, 10, 11)),
            new RemoteRow<NpcSpawnRow>(3, new NpcSpawnRow(2, 5, 20, 21)),
            new RemoteRow<NpcSpawnRow>(4, new NpcSpawnRow(3, 5, 30, 31)),
            new RemoteRow<NpcSpawnRow>(9, new NpcSpawnRow(4, 5, 40, 41)),
            new RemoteRow<NpcSpawnRow>(12, new NpcSpawnRow(5, 5, 50, 51))
        };
        var desired = new[] { new NpcSpawnRow(3, 5, 30, 31) };

        var plan = planner.PlanSpawnReplacement(remote, desired, 5);

        Assert.Equal(new[] { new RowDelete(12), new RowDelete(9), new RowDelete(3), new RowDelete(2) }, plan.Deletes);
        Assert.Empty(plan.Inserts);
    }

    [Fact]
    public void PlanSpawn_Inserts_PreserveDesiredOrder()
    {
        var planner = Planner();
        var remote = new[] { new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(1, 5, 10, 11)) };
        var desired = new[]
        {
            new NpcSpawnRow(2, 5, 20, 21),
            new NpcSpawnRow(3, 5, 30, 31),
            new NpcSpawnRow(1, 5, 10, 11),
            new NpcSpawnRow(4, 5, 40, 41)
        };

        var plan = planner.PlanSpawnReplacement(remote, desired, 5);

        Assert.Empty(plan.Deletes);
        Assert.Equal(
            new[]
            {
                new[] { "2", "5", "21", "22" },
                new[] { "3", "5", "31", "32" },
                new[] { "4", "5", "41", "42" }
            },
            plan.Inserts.Select(i => i.CellValues));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-3)]
    public void PlanSpawn_HeaderRowNumber_Throws(int rowNumber)
    {
        var planner = Planner();
        var remote = new[] { new RemoteRow<NpcSpawnRow>(rowNumber, new NpcSpawnRow(1, 5, 10, 11)) };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => planner.PlanSpawnReplacement(remote, Array.Empty<NpcSpawnRow>(), 5));
    }

    [Fact]
    public void PlanSpawn_WithReorderedSchema_InsertsUseDescriptorPositions()
    {
        var (planner, schema) = ReorderedPlanner();

        var remote = new[] { new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(1, 5, 10, 11)) };
        var desired = new[] { new NpcSpawnRow(7, 5, 2, 3) };

        var plan = planner.PlanSpawnReplacement(remote, desired, 5);

        Assert.Single(plan.Inserts);
        var cells = plan.Inserts[0].CellValues;
        var spawns = schema.GetRequiredSheet("NPC Spawns");
        Assert.Equal(spawns.Columns.Count, cells.Count);
        Assert.Equal("7", cells[spawns.GetColumnIndex("npc_id")]);
        Assert.Equal("5", cells[spawns.GetColumnIndex("map_id")]);
        Assert.Equal("3", cells[spawns.GetColumnIndex("map_x")]);
        Assert.Equal("4", cells[spawns.GetColumnIndex("map_y")]);
    }

    [Fact]
    public void PlanWarp_ReorderedNoOp_YieldsEmptyOperations()
    {
        var planner = Planner();
        var remote = new[]
        {
            new RemoteRow<WarpRow>(2, new WarpRow(5, 10, 11, 1, 12, 13)),
            new RemoteRow<WarpRow>(3, new WarpRow(5, 20, 21, 2, 22, 23))
        };
        var desired = new[]
        {
            new WarpRow(5, 20, 21, 2, 22, 23),
            new WarpRow(5, 10, 11, 1, 12, 13)
        };

        var plan = planner.PlanWarpReplacement(remote, desired, 5);

        Assert.Equal("Warptiles", plan.Sheet);
        Assert.Empty(plan.Deletes);
        Assert.Empty(plan.Inserts);
    }

    [Fact]
    public void PlanWarp_OneChangedRow_YieldsDeleteAndInsert()
    {
        var planner = Planner();
        var remote = new[]
        {
            new RemoteRow<WarpRow>(2, new WarpRow(5, 10, 11, 1, 12, 13)),
            new RemoteRow<WarpRow>(3, new WarpRow(5, 20, 21, 2, 22, 23))
        };
        var desired = new[]
        {
            new WarpRow(5, 10, 11, 1, 99, 99),
            new WarpRow(5, 20, 21, 2, 22, 23)
        };

        var plan = planner.PlanWarpReplacement(remote, desired, 5);

        Assert.Equal(new[] { new RowDelete(2) }, plan.Deletes);
        Assert.Single(plan.Inserts);
        Assert.Equal(new[] { "5", "11", "12", "1", "100", "100" }, plan.Inserts[0].CellValues);
    }

    [Fact]
    public void PlanWarp_DuplicateCountIncrease_InsertsOnly()
    {
        var planner = Planner();
        var remote = new[] { new RemoteRow<WarpRow>(2, new WarpRow(5, 10, 11, 1, 12, 13)) };
        var desired = new[]
        {
            new WarpRow(5, 10, 11, 1, 12, 13),
            new WarpRow(5, 10, 11, 1, 12, 13)
        };

        var plan = planner.PlanWarpReplacement(remote, desired, 5);

        Assert.Empty(plan.Deletes);
        Assert.Equal(
            new[] { new[] { "5", "11", "12", "1", "13", "14" } },
            plan.Inserts.Select(i => i.CellValues));
    }

    [Fact]
    public void PlanWarp_DuplicateCountDecrease_DeletesExactlyOneOccurrence()
    {
        var planner = Planner();
        var remote = new[]
        {
            new RemoteRow<WarpRow>(2, new WarpRow(5, 10, 11, 1, 12, 13)),
            new RemoteRow<WarpRow>(7, new WarpRow(5, 10, 11, 1, 12, 13))
        };
        var desired = new[] { new WarpRow(5, 10, 11, 1, 12, 13) };

        var plan = planner.PlanWarpReplacement(remote, desired, 5);

        Assert.Equal(new[] { new RowDelete(7) }, plan.Deletes);
        Assert.Empty(plan.Inserts);
    }

    [Fact]
    public void PlanWarp_InterleavedOtherMapRows_AreNeverDeleted()
    {
        var planner = Planner();
        var remote = new[]
        {
            new RemoteRow<WarpRow>(2, new WarpRow(5, 10, 11, 1, 12, 13)),
            new RemoteRow<WarpRow>(3, new WarpRow(9, 1, 2, 1, 3, 4)),
            new RemoteRow<WarpRow>(4, new WarpRow(5, 20, 21, 2, 22, 23)),
            new RemoteRow<WarpRow>(5, new WarpRow(9, 3, 4, 2, 5, 6))
        };
        var desired = new[] { new WarpRow(5, 10, 11, 1, 12, 13) };

        var plan = planner.PlanWarpReplacement(remote, desired, 5);

        Assert.Equal(new[] { new RowDelete(4) }, plan.Deletes);
        Assert.Empty(plan.Inserts);
    }

    [Fact]
    public void PlanWarp_OtherMapDesiredRows_AreNeverInserted()
    {
        var planner = Planner();
        var remote = new[] { new RemoteRow<WarpRow>(2, new WarpRow(5, 10, 11, 1, 12, 13)) };
        var desired = new[]
        {
            new WarpRow(9, 1, 2, 1, 3, 4),
            new WarpRow(5, 10, 11, 1, 12, 13),
            new WarpRow(5, 20, 21, 2, 22, 23)
        };

        var plan = planner.PlanWarpReplacement(remote, desired, 5);

        Assert.Empty(plan.Deletes);
        Assert.Equal(new[] { new[] { "5", "21", "22", "2", "23", "24" } }, plan.Inserts.Select(i => i.CellValues));
    }

    [Fact]
    public void PlanWarp_MultipleUnmatchedDeletes_AreDescending()
    {
        var planner = Planner();
        var remote = new[]
        {
            new RemoteRow<WarpRow>(2, new WarpRow(5, 10, 11, 1, 12, 13)),
            new RemoteRow<WarpRow>(3, new WarpRow(5, 20, 21, 2, 22, 23)),
            new RemoteRow<WarpRow>(9, new WarpRow(5, 30, 31, 3, 32, 33)),
            new RemoteRow<WarpRow>(12, new WarpRow(5, 40, 41, 4, 42, 43))
        };
        var desired = new[] { new WarpRow(5, 30, 31, 3, 32, 33) };

        var plan = planner.PlanWarpReplacement(remote, desired, 5);

        Assert.Equal(new[] { new RowDelete(12), new RowDelete(3), new RowDelete(2) }, plan.Deletes);
        Assert.Empty(plan.Inserts);
    }

    [Fact]
    public void PlanWarp_Inserts_PreserveDesiredOrder()
    {
        var planner = Planner();
        var remote = new[] { new RemoteRow<WarpRow>(2, new WarpRow(5, 10, 11, 1, 12, 13)) };
        var desired = new[]
        {
            new WarpRow(5, 20, 21, 2, 22, 23),
            new WarpRow(5, 30, 31, 3, 32, 33),
            new WarpRow(5, 10, 11, 1, 12, 13),
            new WarpRow(5, 40, 41, 4, 42, 43)
        };

        var plan = planner.PlanWarpReplacement(remote, desired, 5);

        Assert.Empty(plan.Deletes);
        Assert.Equal(
            new[]
            {
                new[] { "5", "21", "22", "2", "23", "24" },
                new[] { "5", "31", "32", "3", "33", "34" },
                new[] { "5", "41", "42", "4", "43", "44" }
            },
            plan.Inserts.Select(i => i.CellValues));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-3)]
    public void PlanWarp_HeaderRowNumber_Throws(int rowNumber)
    {
        var planner = Planner();
        var remote = new[] { new RemoteRow<WarpRow>(rowNumber, new WarpRow(5, 10, 11, 1, 12, 13)) };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => planner.PlanWarpReplacement(remote, Array.Empty<WarpRow>(), 5));
    }

    [Fact]
    public void PlanWarp_WithReorderedSchema_InsertsUseDescriptorPositions()
    {
        var (planner, schema) = ReorderedPlanner();

        var remote = new[] { new RemoteRow<WarpRow>(2, new WarpRow(5, 10, 11, 1, 12, 13)) };
        var desired = new[] { new WarpRow(5, 2, 3, 9, 10, 11) };

        var plan = planner.PlanWarpReplacement(remote, desired, 5);

        Assert.Single(plan.Inserts);
        var cells = plan.Inserts[0].CellValues;
        var warptiles = schema.GetRequiredSheet("Warptiles");
        Assert.Equal(warptiles.Columns.Count, cells.Count);
        Assert.Equal("5", cells[warptiles.GetColumnIndex("map_id")]);
        Assert.Equal("3", cells[warptiles.GetColumnIndex("map_x")]);
        Assert.Equal("4", cells[warptiles.GetColumnIndex("map_y")]);
        Assert.Equal("9", cells[warptiles.GetColumnIndex("warp_id")]);
        Assert.Equal("11", cells[warptiles.GetColumnIndex("warp_x")]);
        Assert.Equal("12", cells[warptiles.GetColumnIndex("warp_y")]);
    }

    [Fact]
    public void Plan_DeletesAndInserts_CannotBeCastToMutableLists()
    {
        var planner = Planner();
        var remote = new[] { new RemoteRow<NpcSpawnRow>(2, new NpcSpawnRow(1, 5, 10, 11)) };
        var desired = new[] { new NpcSpawnRow(1, 5, 99, 99) };

        var plan = planner.PlanSpawnReplacement(remote, desired, 5);

        Assert.Single(plan.Deletes);
        Assert.Single(plan.Inserts);
        Assert.Throws<InvalidCastException>(() => (List<RowDelete>)plan.Deletes);
        Assert.Throws<InvalidCastException>(() => (List<RowInsert>)plan.Inserts);
    }

    private static ReplacementPlanner Planner() =>
        new(new SheetRowMapper(GameDataSchema.LoadEmbedded()));

    private static (ReplacementPlanner Planner, GameDataSchema Schema) ReorderedPlanner()
    {
        var json = ReorderedSchemaJson();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var schema = GameDataSchema.Load(stream);
        return (new ReplacementPlanner(new SheetRowMapper(schema)), schema);
    }

    private static string ReorderedSchemaJson() => """
        {"sheets":[
          {"sheet":"NPCs","table":"npc_templates","columns":[
            {"name":"npc_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true},
            {"name":"npc_name","header":"name","kind":"Text","sql":"TEXT","required":true,"pk":false},
            {"name":"body_state","header":"bs","kind":"Int","sql":"SMALLINT","default":"3","required":false,"pk":false},
            {"name":"body_id","header":"bid","kind":"Int","sql":"SMALLINT","default":"1","required":false,"pk":false},
            {"name":"body_r","header":"br","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"body_g","header":"bg","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"body_b","header":"bb","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"body_a","header":"ba","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"face_id","header":"face","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"hair_id","header":"hair","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"hair_r","header":"hr","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"hair_g","header":"hg","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"hair_b","header":"hb","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"hair_a","header":"ha","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"equipped_items","header":"items","kind":"Text","sql":"TEXT","default":"'0,*,0,*,0,*,0,*,0,*,0,*'","required":false,"pk":false}
          ]},
          {"sheet":"NPC Spawns","table":"npc_spawns","columns":[
            {"name":"map_y","header":"y","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"npc_id","header":"npc","kind":"Id","sql":"INT","required":true,"pk":false},
            {"name":"map_x","header":"x","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_id","header":"map","kind":"Id","sql":"SMALLINT","required":true,"pk":false}
          ]},
          {"sheet":"Warptiles","table":"warptiles","columns":[
            {"name":"warp_y","header":"wy","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_id","header":"map","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_id","header":"wid","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_y","header":"y","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_x","header":"wx","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_x","header":"x","kind":"Int","sql":"SMALLINT","required":true,"pk":false}
          ]},
          {"sheet":"Maps","table":"maps","columns":[
            {"name":"map_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true},
            {"name":"map_name","header":"name","kind":"Text","sql":"TEXT","required":true,"pk":false},
            {"name":"map_filename","header":"fn","kind":"Text","sql":"TEXT","required":true,"pk":false}
          ]}
        ]}
        """;
}
