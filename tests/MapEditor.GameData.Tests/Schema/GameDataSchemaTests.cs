using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MapEditor.GameData.Schema;
using Xunit;

namespace MapEditor.GameData.Tests.Schema;

public class GameDataSchemaTests
{
    [Fact]
    public void LoadEmbedded_ContainsRequiredSheetsInGeneratedOrder()
    {
        var schema = GameDataSchema.LoadEmbedded();

        Assert.Equal(new[] { "NPCs", "NPC Spawns", "Warptiles", "Maps" },
            schema.Sheets.Select(s => s.Sheet).ToArray());
        Assert.Equal(new[] { "npc_templates", "npc_spawns", "warptiles", "maps" },
            schema.Sheets.Select(s => s.Table).ToArray());
    }

    [Fact]
    public void LoadEmbedded_ContainsExactColumnMetadata()
    {
        var schema = GameDataSchema.LoadEmbedded();

        var npcId = schema.GetRequiredSheet("NPCs").GetRequiredColumn("npc_id");
        Assert.Equal("ID", npcId.Header);
        Assert.Equal("Id", npcId.Kind);
        Assert.Equal("INTEGER", npcId.Sql);
        Assert.Null(npcId.Default);
        Assert.True(npcId.Required);
        Assert.True(npcId.IsPrimaryKey);
        Assert.Null(npcId.RefSheet);

        var npcType = schema.GetRequiredSheet("NPCs").GetRequiredColumn("npc_type");
        Assert.Equal("type (Monster)", npcType.Header);
        Assert.Equal("Enum", npcType.Kind);
        Assert.Equal("SMALLINT", npcType.Sql);
        Assert.Equal("2", npcType.Default);
        Assert.False(npcType.Required);
        Assert.False(npcType.IsPrimaryKey);

        var warp = schema.GetRequiredSheet("Warptiles").GetRequiredColumn("warp_id");
        Assert.Equal("warp to map id", warp.Header);
        Assert.Equal("Maps", warp.RefSheet);

        var mapId = schema.GetRequiredSheet("Maps").GetRequiredColumn("map_id");
        Assert.True(mapId.IsPrimaryKey);
        Assert.Equal("id", mapId.Header);
    }

    [Fact]
    public void LoadEmbedded_PublishesNonListCollections()
    {
        var schema = GameDataSchema.LoadEmbedded();

        Assert.True(schema.Sheets is not List<SheetSchema>);
        Assert.True(schema.Sheets[0].Columns is not List<ColumnSchema>);
    }

    [Fact]
    public void GetRequiredSheet_ReturnsSheetByOrdinalName()
    {
        var schema = GameDataSchema.LoadEmbedded();

        var sheet = schema.GetRequiredSheet("NPC Spawns");
        Assert.Equal("npc_spawns", sheet.Table);
        Assert.Equal(4, sheet.Columns.Count);
    }

    [Fact]
    public void GetRequiredSheet_WithMissingName_ThrowsKeyNotFoundException()
    {
        var schema = GameDataSchema.LoadEmbedded();

        Assert.Throws<KeyNotFoundException>(() => schema.GetRequiredSheet("Classes"));
    }

    [Fact]
    public void GetColumnIndex_ReturnsPositionalIndex()
    {
        var sheet = GameDataSchema.LoadEmbedded().GetRequiredSheet("NPC Spawns");

        Assert.Equal(0, sheet.GetColumnIndex("npc_id"));
        Assert.Equal(3, sheet.GetColumnIndex("map_y"));
    }

    [Fact]
    public void GetRequiredColumn_WithMissingName_ThrowsKeyNotFoundException()
    {
        var sheet = GameDataSchema.LoadEmbedded().GetRequiredSheet("Maps");

        Assert.Throws<KeyNotFoundException>(() => sheet.GetRequiredColumn("nope"));
        Assert.Throws<KeyNotFoundException>(() => sheet.GetColumnIndex("nope"));
    }

    [Fact]
    public void Load_WithMalformedJson_ThrowsInvalidDataException()
    {
        using var stream = StreamOf("{ this is not json");

        Assert.Throws<InvalidDataException>(() => GameDataSchema.Load(stream));
    }

    [Fact]
    public void Load_WithDuplicateSheetNames_ThrowsInvalidDataException()
    {
        var json = """
        {"sheets":[
          {"sheet":"Maps","table":"maps","columns":[{"name":"map_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true}]},
          {"sheet":"Maps","table":"maps2","columns":[{"name":"map_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true}]}
        ]}
        """;

        using var stream = StreamOf(json);

        var ex = Assert.Throws<InvalidDataException>(() => GameDataSchema.Load(stream));
        Assert.Contains("Maps", ex.Message);
    }

    [Fact]
    public void Load_WithBlankSheetName_ThrowsInvalidDataException()
    {
        var json = """
        {"sheets":[{"sheet":"  ","table":"maps","columns":[]}]}
        """;

        using var stream = StreamOf(json);

        Assert.Throws<InvalidDataException>(() => GameDataSchema.Load(stream));
    }

    [Fact]
    public void Load_WithDuplicateColumnNames_ThrowsInvalidDataException()
    {
        var json = """
        {"sheets":[{"sheet":"Maps","table":"maps","columns":[
          {"name":"map_id","header":"id","kind":"Id","sql":"INTEGER","required":true,"pk":true},
          {"name":"map_id","header":"dup","kind":"Text","sql":"TEXT","required":true,"pk":false}
        ]}]}
        """;

        using var stream = StreamOf(json);

        var ex = Assert.Throws<InvalidDataException>(() => GameDataSchema.Load(stream));
        Assert.Contains("map_id", ex.Message);
        Assert.Contains("Maps", ex.Message);
    }

    [Fact]
    public void Load_WithMissingHeader_ThrowsInvalidDataException()
    {
        var json = """
        {"sheets":[{"sheet":"Maps","table":"maps","columns":[
          {"name":"map_id","kind":"Id","sql":"INTEGER","required":true,"pk":true}
        ]}]}
        """;

        using var stream = StreamOf(json);

        var ex = Assert.Throws<InvalidDataException>(() => GameDataSchema.Load(stream));
        Assert.Contains("map_id", ex.Message);
    }

    [Fact]
    public void Load_WithBlankHeader_ThrowsInvalidDataException()
    {
        var json = """
        {"sheets":[{"sheet":"Maps","table":"maps","columns":[
          {"name":"map_id","header":"   ","kind":"Id","sql":"INTEGER","required":true,"pk":true}
        ]}]}
        """;

        using var stream = StreamOf(json);

        var ex = Assert.Throws<InvalidDataException>(() => GameDataSchema.Load(stream));
        Assert.Contains("map_id", ex.Message);
    }

    [Fact]
    public void Load_WithMissingSheetName_ThrowsInvalidDataException()
    {
        var json = """
        {"sheets":[{"table":"maps","columns":[]}]}
        """;

        using var stream = StreamOf(json);

        Assert.Throws<InvalidDataException>(() => GameDataSchema.Load(stream));
    }

    [Fact]
    public void Load_WithMissingSheets_ThrowsInvalidDataException()
    {
        using var stream = StreamOf("{}");

        Assert.Throws<InvalidDataException>(() => GameDataSchema.Load(stream));
    }

    private static MemoryStream StreamOf(string json) =>
        new(Encoding.UTF8.GetBytes(json));
}
