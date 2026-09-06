using System;
using System.IO;
using System.Linq;
using System.Text;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Schema;
using Xunit;

namespace MapEditor.GameData.Tests.Rows;

public class SheetRowMapperTests
{
    [Fact]
    public void MapMap_ReadsRequiredColumns()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var maps = schema.GetRequiredSheet("Maps");

        var row = Row(maps, ("map_id", "42"), ("map_name", "Test Map"), ("map_filename", "maps/test.map"));

        var reference = mapper.MapMap(row);

        Assert.Equal(new MapReference(42, "Test Map", "maps/test.map"), reference);
    }

    [Fact]
    public void MapNpc_ReadsConsumedColumns()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var npcs = schema.GetRequiredSheet("NPCs");

        var row = Row(npcs,
            ("npc_id", "7"),
            ("npc_name", "Goblin"),
            ("body_state", "5"),
            ("body_id", "12"),
            ("body_r", "10"),
            ("body_g", "20"),
            ("body_b", "30"),
            ("body_a", "255"),
            ("face_id", "3"),
            ("hair_id", "4"),
            ("hair_r", "1"),
            ("hair_g", "2"),
            ("hair_b", "3"),
            ("hair_a", "4"),
            ("equipped_items", "1,*,2,*,0,*,0,*,0,*,0,*"));

        var npc = mapper.MapNpc(row);

        Assert.Equal(new NpcAppearance(
            7,
            "Goblin",
            5,
            12,
            new RgbaValue(10, 20, 30, 255),
            3,
            4,
            new RgbaValue(1, 2, 3, 4),
            "1,*,2,*,0,*,0,*,0,*,0,*"), npc);
    }

    [Fact]
    public void MapSpawn_ReadsRequiredColumns()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var spawns = schema.GetRequiredSheet("NPC Spawns");

        var row = Row(spawns, ("npc_id", "7"), ("map_id", "1"), ("map_x", "2"), ("map_y", "3"));

        Assert.Equal(new NpcSpawnRow(7, 1, 2, 3), mapper.MapSpawn(row));
    }

    [Fact]
    public void MapWarp_ReadsRequiredColumns()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var warptiles = schema.GetRequiredSheet("Warptiles");

        var row = Row(warptiles,
            ("map_id", "1"),
            ("map_x", "2"),
            ("map_y", "3"),
            ("warp_id", "9"),
            ("warp_x", "10"),
            ("warp_y", "11"));

        Assert.Equal(new WarpRow(1, 2, 3, 9, 10, 11), mapper.MapWarp(row));
    }

    [Fact]
    public void Map_WithReorderedSchema_FollowsDescriptorNamesNotPositions()
    {
        var json = """
        {"sheets":[
          {"sheet":"NPCs","table":"npc_templates","columns":[
            {"name":"hair_a","header":"a","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"body_a","header":"ba","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"equipped_items","header":"items","kind":"Text","sql":"TEXT","default":"'0,*,0,*,0,*,0,*,0,*,0,*'","required":false,"pk":false},
            {"name":"body_state","header":"bs","kind":"Int","sql":"SMALLINT","default":"3","required":false,"pk":false},
            {"name":"npc_name","header":"name","kind":"Text","sql":"TEXT","required":true,"pk":false},
            {"name":"face_id","header":"face","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"body_r","header":"br","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"hair_id","header":"hair","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"npc_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true},
            {"name":"body_g","header":"bg","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"hair_r","header":"hr","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"body_b","header":"bb","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"hair_g","header":"hg","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false},
            {"name":"body_id","header":"bid","kind":"Int","sql":"SMALLINT","default":"1","required":false,"pk":false},
            {"name":"hair_b","header":"hb","kind":"Int","sql":"SMALLINT","default":"0","required":false,"pk":false}
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
            {"name":"map_filename","header":"fn","kind":"Text","sql":"TEXT","required":true,"pk":false},
            {"name":"map_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true},
            {"name":"map_name","header":"name","kind":"Text","sql":"TEXT","required":true,"pk":false}
          ]}
        ]}
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var schema = GameDataSchema.Load(stream);
        var mapper = new SheetRowMapper(schema);

        var maps = schema.GetRequiredSheet("Maps");
        var reference = mapper.MapMap(
            Row(maps, ("map_filename", "m.map"), ("map_id", "9"), ("map_name", "Nine")));
        Assert.Equal(new MapReference(9, "Nine", "m.map"), reference);

        var npcs = schema.GetRequiredSheet("NPCs");
        var npc = mapper.MapNpc(
            Row(npcs, ("npc_id", "7"), ("npc_name", "Goblin"), ("body_state", "5"), ("hair_a", "9")));
        Assert.Equal(new NpcAppearance(
            7,
            "Goblin",
            5,
            1,
            new RgbaValue(0, 0, 0, 0),
            0,
            0,
            new RgbaValue(0, 0, 0, 9),
            "0,*,0,*,0,*,0,*,0,*,0,*"), npc);

        var spawns = schema.GetRequiredSheet("NPC Spawns");
        var spawn = mapper.MapSpawn(
            Row(spawns, ("map_y", "3"), ("npc_id", "7"), ("map_x", "2"), ("map_id", "1")));
        Assert.Equal(new NpcSpawnRow(7, 1, 2, 3), spawn);

        var warptiles = schema.GetRequiredSheet("Warptiles");
        var warp = mapper.MapWarp(
            Row(warptiles,
                ("warp_y", "11"),
                ("map_id", "1"),
                ("warp_id", "9"),
                ("map_y", "3"),
                ("warp_x", "10"),
                ("map_x", "2")));
        Assert.Equal(new WarpRow(1, 2, 3, 9, 10, 11), warp);
    }

    [Fact]
    public void MapMap_WithBlankRequiredText_ThrowsFormatException()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var maps = schema.GetRequiredSheet("Maps");

        var row = Row(maps, ("map_id", "42"), ("map_name", "   "), ("map_filename", "m.map"));

        var ex = Assert.Throws<FormatException>(() => mapper.MapMap(row));
        Assert.Contains("Maps", ex.Message);
        Assert.Contains("map_name", ex.Message);

        var missing = Row(maps, ("map_id", "42"), ("map_filename", "m.map"));
        Assert.Throws<FormatException>(() => mapper.MapMap(missing));
    }

    [Fact]
    public void MapSpawn_WithBlankRequiredInteger_ThrowsFormatException()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var spawns = schema.GetRequiredSheet("NPC Spawns");

        var row = Row(spawns, ("npc_id", "7"), ("map_id", "1"), ("map_y", "3"));

        var ex = Assert.Throws<FormatException>(() => mapper.MapSpawn(row));
        Assert.Contains("NPC Spawns", ex.Message);
        Assert.Contains("map_x", ex.Message);
    }

    [Fact]
    public void MapMap_WithMalformedInteger_ThrowsFormatException()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var maps = schema.GetRequiredSheet("Maps");

        var row = Row(maps, ("map_id", "abc"), ("map_name", "Test Map"), ("map_filename", "m.map"));

        var ex = Assert.Throws<FormatException>(() => mapper.MapMap(row));
        Assert.Contains("Maps", ex.Message);
        Assert.Contains("map_id", ex.Message);
        Assert.Contains("abc", ex.Message);
    }

    [Fact]
    public void MapNpc_WithBlankOptionalCells_AppliesDescriptorDefaults()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var npcs = schema.GetRequiredSheet("NPCs");

        var row = Row(npcs, ("npc_id", "7"), ("npc_name", "Goblin"));

        var npc = mapper.MapNpc(row);

        Assert.Equal(3, npc.BodyState);
        Assert.Equal(1, npc.BodyId);
        Assert.Equal(new RgbaValue(0, 0, 0, 0), npc.BodyTint);
        Assert.Equal(0, npc.FaceId);
        Assert.Equal(0, npc.HairId);
        Assert.Equal(new RgbaValue(0, 0, 0, 0), npc.HairTint);
        Assert.Equal("0,*,0,*,0,*,0,*,0,*,0,*", npc.EquippedItems);
    }

    [Fact]
    public void MapNpc_WithShortRow_AppliesDescriptorDefaultsForMissingCells()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var npcs = schema.GetRequiredSheet("NPCs");

        var full = Row(npcs,
            ("npc_id", "7"),
            ("npc_name", "Goblin"),
            ("body_state", "5"),
            ("body_id", "12"));
        var shortRow = full.Take(npcs.GetColumnIndex("body_id") + 1).ToArray();

        var npc = mapper.MapNpc(shortRow);

        Assert.Equal(5, npc.BodyState);
        Assert.Equal(12, npc.BodyId);
        Assert.Equal(new RgbaValue(0, 0, 0, 0), npc.BodyTint);
        Assert.Equal("0,*,0,*,0,*,0,*,0,*,0,*", npc.EquippedItems);
    }

    [Fact]
    public void MapSpawn_WithShortRow_ThrowsFormatException()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var spawns = schema.GetRequiredSheet("NPC Spawns");

        var full = Row(spawns, ("npc_id", "7"), ("map_id", "1"), ("map_x", "2"), ("map_y", "3"));
        var shortRow = full.Take(2).ToArray();

        Assert.Throws<FormatException>(() => mapper.MapSpawn(shortRow));
    }

    [Fact]
    public void MapSpawn_WithExtraCells_IgnoresThem()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var spawns = schema.GetRequiredSheet("NPC Spawns");

        var full = Row(spawns, ("npc_id", "7"), ("map_id", "1"), ("map_x", "2"), ("map_y", "3"));
        var withExtras = new string?[full.Length + 2];
        full.CopyTo(withExtras, 0);
        withExtras[full.Length] = "junk";
        withExtras[full.Length + 1] = "more junk";

        Assert.Equal(new NpcSpawnRow(7, 1, 2, 3), mapper.MapSpawn(withExtras));
    }

    [Fact]
    public void ToCells_Spawn_PreservesValuesAndRoundTrips()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var spawns = schema.GetRequiredSheet("NPC Spawns");

        var row = new NpcSpawnRow(7, 1, 2, 3);
        var cells = mapper.ToCells(row);

        Assert.Equal(spawns.Columns.Count, cells.Count);
        Assert.Equal("7", cells[spawns.GetColumnIndex("npc_id")]);
        Assert.Equal("1", cells[spawns.GetColumnIndex("map_id")]);
        Assert.Equal("2", cells[spawns.GetColumnIndex("map_x")]);
        Assert.Equal("3", cells[spawns.GetColumnIndex("map_y")]);
        Assert.Equal(row, mapper.MapSpawn(cells));
    }

    [Fact]
    public void ToCells_Warp_PreservesValuesAndRoundTrips()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);
        var warptiles = schema.GetRequiredSheet("Warptiles");

        var row = new WarpRow(1, 2, 3, 9, 10, 11);
        var cells = mapper.ToCells(row);

        Assert.Equal(warptiles.Columns.Count, cells.Count);
        Assert.Equal("1", cells[warptiles.GetColumnIndex("map_id")]);
        Assert.Equal("2", cells[warptiles.GetColumnIndex("map_x")]);
        Assert.Equal("3", cells[warptiles.GetColumnIndex("map_y")]);
        Assert.Equal("9", cells[warptiles.GetColumnIndex("warp_id")]);
        Assert.Equal("10", cells[warptiles.GetColumnIndex("warp_x")]);
        Assert.Equal("11", cells[warptiles.GetColumnIndex("warp_y")]);
        Assert.Equal(row, mapper.MapWarp(cells));
    }

    [Fact]
    public void ToCells_Result_CannotBeCastToArray()
    {
        var schema = GameDataSchema.LoadEmbedded();
        var mapper = new SheetRowMapper(schema);

        var spawnCells = mapper.ToCells(new NpcSpawnRow(7, 1, 2, 3));
        var warpCells = mapper.ToCells(new WarpRow(1, 2, 3, 9, 10, 11));

        Assert.Throws<InvalidCastException>(() => (string?[])spawnCells);
        Assert.Throws<InvalidCastException>(() => (string?[])warpCells);
    }

    [Fact]
    public void ToCells_WithReorderedSchema_PlacesValuesAtDescriptorIndexes()
    {
        var json = """
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

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var schema = GameDataSchema.Load(stream);
        var mapper = new SheetRowMapper(schema);
        var spawns = schema.GetRequiredSheet("NPC Spawns");

        var cells = mapper.ToCells(new NpcSpawnRow(7, 1, 2, 3));

        Assert.Equal(spawns.Columns.Count, cells.Count);
        Assert.Equal("7", cells[spawns.GetColumnIndex("npc_id")]);
        Assert.Equal("1", cells[spawns.GetColumnIndex("map_id")]);
        Assert.Equal("2", cells[spawns.GetColumnIndex("map_x")]);
        Assert.Equal("3", cells[spawns.GetColumnIndex("map_y")]);
    }

    [Fact]
    public void MapNpc_WithQuotedTextDefaults_UnescapesDoubledQuotes()
    {
        var json = """
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
            {"name":"equipped_items","header":"items","kind":"Text","sql":"TEXT","default":"'''O''Brien''s loadout'''","required":false,"pk":false}
          ]},
          {"sheet":"NPC Spawns","table":"npc_spawns","columns":[
            {"name":"npc_id","header":"npc","kind":"Id","sql":"INT","required":true,"pk":false},
            {"name":"map_id","header":"map","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_x","header":"x","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_y","header":"y","kind":"Int","sql":"SMALLINT","required":true,"pk":false}
          ]},
          {"sheet":"Warptiles","table":"warptiles","columns":[
            {"name":"map_id","header":"map","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_x","header":"x","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_y","header":"y","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_id","header":"wid","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_x","header":"wx","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_y","header":"wy","kind":"Int","sql":"SMALLINT","required":true,"pk":false}
          ]},
          {"sheet":"Maps","table":"maps","columns":[
            {"name":"map_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true},
            {"name":"map_name","header":"name","kind":"Text","sql":"TEXT","required":true,"pk":false},
            {"name":"map_filename","header":"fn","kind":"Text","sql":"TEXT","required":true,"pk":false}
          ]}
        ]}
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var schema = GameDataSchema.Load(stream);
        var mapper = new SheetRowMapper(schema);
        var npcs = schema.GetRequiredSheet("NPCs");

        var npc = mapper.MapNpc(Row(npcs, ("npc_id", "7"), ("npc_name", "Goblin")));

        Assert.Equal("'O'Brien's loadout'", npc.EquippedItems);
    }

    [Fact]
    public void MapNpc_WithUnsupportedIntegerDefault_ThrowsFormatException()
    {
        var json = """
        {"sheets":[
          {"sheet":"NPCs","table":"npc_templates","columns":[
            {"name":"npc_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true},
            {"name":"npc_name","header":"name","kind":"Text","sql":"TEXT","required":true,"pk":false},
            {"name":"body_state","header":"bs","kind":"Int","sql":"SMALLINT","default":"NULL","required":false,"pk":false},
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
            {"name":"npc_id","header":"npc","kind":"Id","sql":"INT","required":true,"pk":false},
            {"name":"map_id","header":"map","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_x","header":"x","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_y","header":"y","kind":"Int","sql":"SMALLINT","required":true,"pk":false}
          ]},
          {"sheet":"Warptiles","table":"warptiles","columns":[
            {"name":"map_id","header":"map","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_x","header":"x","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_y","header":"y","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_id","header":"wid","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_x","header":"wx","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_y","header":"wy","kind":"Int","sql":"SMALLINT","required":true,"pk":false}
          ]},
          {"sheet":"Maps","table":"maps","columns":[
            {"name":"map_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true},
            {"name":"map_name","header":"name","kind":"Text","sql":"TEXT","required":true,"pk":false},
            {"name":"map_filename","header":"fn","kind":"Text","sql":"TEXT","required":true,"pk":false}
          ]}
        ]}
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var schema = GameDataSchema.Load(stream);
        var mapper = new SheetRowMapper(schema);
        var npcs = schema.GetRequiredSheet("NPCs");

        var row = Row(npcs, ("npc_id", "7"), ("npc_name", "Goblin"));

        var ex = Assert.Throws<FormatException>(() => mapper.MapNpc(row));
        Assert.Contains("NPCs", ex.Message);
        Assert.Contains("body_state", ex.Message);
        Assert.Contains("NULL", ex.Message);
    }

    [Fact]
    public void MapNpc_WithOddQuoteCountTextDefault_ThrowsFormatException()
    {
        var json = """
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
            {"name":"equipped_items","header":"items","kind":"Text","sql":"TEXT","default":"'''","required":false,"pk":false}
          ]},
          {"sheet":"NPC Spawns","table":"npc_spawns","columns":[
            {"name":"npc_id","header":"npc","kind":"Id","sql":"INT","required":true,"pk":false},
            {"name":"map_id","header":"map","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_x","header":"x","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_y","header":"y","kind":"Int","sql":"SMALLINT","required":true,"pk":false}
          ]},
          {"sheet":"Warptiles","table":"warptiles","columns":[
            {"name":"map_id","header":"map","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_x","header":"x","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_y","header":"y","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_id","header":"wid","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_x","header":"wx","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_y","header":"wy","kind":"Int","sql":"SMALLINT","required":true,"pk":false}
          ]},
          {"sheet":"Maps","table":"maps","columns":[
            {"name":"map_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true},
            {"name":"map_name","header":"name","kind":"Text","sql":"TEXT","required":true,"pk":false},
            {"name":"map_filename","header":"fn","kind":"Text","sql":"TEXT","required":true,"pk":false}
          ]}
        ]}
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var schema = GameDataSchema.Load(stream);
        var mapper = new SheetRowMapper(schema);
        var npcs = schema.GetRequiredSheet("NPCs");

        var row = Row(npcs, ("npc_id", "7"), ("npc_name", "Goblin"));

        var ex = Assert.Throws<FormatException>(() => mapper.MapNpc(row));
        Assert.Contains("NPCs", ex.Message);
        Assert.Contains("equipped_items", ex.Message);
    }

    [Fact]
    public void Ctor_WithMissingConsumedColumn_ThrowsKeyNotFoundException()
    {
        var json = """
        {"sheets":[
          {"sheet":"NPCs","table":"npc_templates","columns":[
            {"name":"npc_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true}
          ]},
          {"sheet":"NPC Spawns","table":"npc_spawns","columns":[
            {"name":"npc_id","header":"npc","kind":"Id","sql":"INT","required":true,"pk":false},
            {"name":"map_id","header":"map","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_x","header":"x","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_y","header":"y","kind":"Int","sql":"SMALLINT","required":true,"pk":false}
          ]},
          {"sheet":"Warptiles","table":"warptiles","columns":[
            {"name":"map_id","header":"map","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_x","header":"x","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"map_y","header":"y","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_id","header":"wid","kind":"Id","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_x","header":"wx","kind":"Int","sql":"SMALLINT","required":true,"pk":false},
            {"name":"warp_y","header":"wy","kind":"Int","sql":"SMALLINT","required":true,"pk":false}
          ]},
          {"sheet":"Maps","table":"maps","columns":[
            {"name":"map_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true},
            {"name":"map_name","header":"name","kind":"Text","sql":"TEXT","required":true,"pk":false},
            {"name":"map_filename","header":"fn","kind":"Text","sql":"TEXT","required":true,"pk":false}
          ]}
        ]}
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
            () => new SheetRowMapper(GameDataSchema.Load(stream)));
    }

    private static string?[] Row(SheetSchema sheet, params (string Name, string Value)[] values)
    {
        var cells = new string?[sheet.Columns.Count];
        foreach (var (name, value) in values)
        {
            cells[sheet.GetColumnIndex(name)] = value;
        }
        return cells;
    }
}
