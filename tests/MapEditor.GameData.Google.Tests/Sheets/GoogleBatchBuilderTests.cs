using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.GameData.Google.Sheets;
using MapEditor.GameData.Replacement;
using Xunit;

namespace MapEditor.GameData.Google.Tests.Sheets;

public class GoogleBatchBuilderTests
{
    private static readonly Dictionary<string, int> SheetIds = new(StringComparer.Ordinal)
    {
        ["NPC Spawns"] = 7,
        ["Warptiles"] = 9
    };

    [Fact]
    public void Build_CombinedPlans_SingleRequestContainsAllSpawnAndWarpOperations()
    {
        var spawnPlan = new ReplacementPlan(
            "NPC Spawns",
            new[] { new RowDelete(3), new RowDelete(5) },
            new[] { new RowInsert(new string?[] { "10", "1", "5", "6" }) });
        var warpPlan = new ReplacementPlan(
            "Warptiles",
            new[] { new RowDelete(4) },
            new[] { new RowInsert(new string?[] { "1", "2", "3", "9", "4", "5" }) });

        var request = GoogleBatchBuilder.Build(SheetIds, spawnPlan, warpPlan);

        var requests = request.Requests;
        Assert.Equal(5, requests.Count);

        Assert.Equal(7, requests[0].DeleteDimension.Range.SheetId);
        Assert.Equal(7, requests[1].DeleteDimension.Range.SheetId);
        Assert.Equal(7, requests[2].AppendCells.SheetId);
        Assert.Equal(9, requests[3].DeleteDimension.Range.SheetId);
        Assert.Equal(9, requests[4].AppendCells.SheetId);
    }

    [Fact]
    public void Build_OneBasedDeleteRow_BecomesZeroBasedSingleRowRowDimension()
    {
        var plan = new ReplacementPlan(
            "NPC Spawns",
            new[] { new RowDelete(11) },
            Array.Empty<RowInsert>());

        var request = GoogleBatchBuilder.Build(SheetIds, plan, EmptyPlan("Warptiles"));

        var range = request.Requests.Single().DeleteDimension.Range;
        Assert.Equal(7, range.SheetId);
        Assert.Equal("ROWS", range.Dimension);
        Assert.Equal(10, range.StartIndex);
        Assert.Equal(11, range.EndIndex);
    }

    [Fact]
    public void Build_UnorderedDeletes_RemainDescendingPerSheetBeforeAppends()
    {
        var plan = new ReplacementPlan(
            "Warptiles",
            new[] { new RowDelete(3), new RowDelete(9), new RowDelete(5) },
            new[] { new RowInsert(new string?[] { "1", "2", "3", "4", "5", "6" }) });

        var request = GoogleBatchBuilder.Build(SheetIds, EmptyPlan("NPC Spawns"), plan);

        var requests = request.Requests;
        Assert.Equal(4, requests.Count);
        var deleteStarts = requests.Take(3).Select(r => r.DeleteDimension.Range.StartIndex).ToList();
        Assert.Equal(new int?[] { 8, 4, 2 }, deleteStarts);
        Assert.All(requests.Take(3), r => Assert.Equal(9, r.DeleteDimension.Range.SheetId));
        Assert.NotNull(requests[3].AppendCells);
        Assert.Equal(9, requests[3].AppendCells.SheetId);
    }

    [Fact]
    public void Build_AppendRows_KeepColumnPositionsForInteriorBlanks_AndOmitTrailingBlanks()
    {
        var plan = new ReplacementPlan(
            "NPC Spawns",
            Array.Empty<RowDelete>(),
            new[]
            {
                new RowInsert(new string?[] { "10", null, "5", "6" }),
                new RowInsert(new string?[] { "10", null, null }),
                new RowInsert(new string?[] { null, "7" }),
                new RowInsert(new string?[] { "11", "1", null, null })
            });

        var request = GoogleBatchBuilder.Build(SheetIds, plan, EmptyPlan("Warptiles"));

        var append = request.Requests.Single().AppendCells;
        Assert.Equal(7, append.SheetId);
        Assert.Equal("userEnteredValue", append.Fields);
        Assert.Equal(4, append.Rows.Count);

        var interior = append.Rows[0].Values;
        Assert.Equal(4, interior.Count);
        Assert.Equal("10", interior[0].UserEnteredValue.StringValue);
        Assert.Null(interior[1].UserEnteredValue);
        Assert.Equal("5", interior[2].UserEnteredValue.StringValue);
        Assert.Equal("6", interior[3].UserEnteredValue.StringValue);

        var trailing = append.Rows[1].Values;
        Assert.Single(trailing);
        Assert.Equal("10", trailing[0].UserEnteredValue.StringValue);

        var leading = append.Rows[2].Values;
        Assert.Equal(2, leading.Count);
        Assert.Null(leading[0].UserEnteredValue);
        Assert.Equal("7", leading[1].UserEnteredValue.StringValue);

        var secondRow = append.Rows[3].Values;
        Assert.Equal(2, secondRow.Count);
        Assert.Equal("11", secondRow[0].UserEnteredValue.StringValue);
        Assert.Equal("1", secondRow[1].UserEnteredValue.StringValue);
    }

    [Fact]
    public void Build_AppendRow_KeepsNonBlankPropertiesAsTheFifthCell()
    {
        const string properties = "{\"facing\":\"north\",\"scale\":2}";
        var plan = new ReplacementPlan(
            "NPC Spawns",
            Array.Empty<RowDelete>(),
            new[] { new RowInsert(new string?[] { "10", "1", "5", "6", properties }) });

        var request = GoogleBatchBuilder.Build(SheetIds, plan, EmptyPlan("Warptiles"));

        var append = request.Requests.Single().AppendCells;
        Assert.Equal(7, append.SheetId);
        var row = Assert.Single(append.Rows).Values;
        Assert.Equal(5, row.Count);
        Assert.Equal(
            new[] { "10", "1", "5", "6", properties },
            row.Select(cell => cell.UserEnteredValue.StringValue).ToArray());
    }

    [Fact]
    public void Build_NeverTargetsUnplannedRowsOrOtherSheetIds()
    {
        var spawnPlan = new ReplacementPlan(
            "NPC Spawns",
            new[] { new RowDelete(2) },
            new[] { new RowInsert(new string?[] { "10", "1", "5", "6" }) });
        var warpPlan = new ReplacementPlan(
            "Warptiles",
            new[] { new RowDelete(6) },
            new[] { new RowInsert(new string?[] { "1", "2", "3", "9", "4", "5" }) });

        var request = GoogleBatchBuilder.Build(SheetIds, spawnPlan, warpPlan);

        var plannedDeletes = new HashSet<long> { 1, 5 };
        foreach (var requestEntry in request.Requests)
        {
            if (requestEntry.DeleteDimension is not null)
            {
                var range = requestEntry.DeleteDimension.Range;
                var sheetId = range.SheetId ?? -1;
                var start = range.StartIndex ?? -1;
                Assert.True(sheetId == 7 || sheetId == 9);
                Assert.Contains(start, plannedDeletes);
                Assert.True(range.EndIndex == start + 1);
            }
            else
            {
                var appendSheetId = requestEntry.AppendCells.SheetId ?? -1;
                Assert.True(appendSheetId == 7 || appendSheetId == 9);
            }
        }
        Assert.Equal(1, request.Requests.Count(r => r.DeleteDimension?.Range.SheetId == 7));
        Assert.Equal(1, request.Requests.Count(r => r.DeleteDimension?.Range.SheetId == 9));
        Assert.Equal(2, request.Requests.Count(r => r.AppendCells is not null));
    }

    [Fact]
    public void Build_EmptyPlans_ProduceNoRequestsForThatSheet()
    {
        var request = GoogleBatchBuilder.Build(SheetIds, EmptyPlan("NPC Spawns"), EmptyPlan("Warptiles"));
        Assert.Empty(request.Requests);

        var spawnPlan = new ReplacementPlan(
            "NPC Spawns",
            new[] { new RowDelete(3) },
            Array.Empty<RowInsert>());
        var partial = GoogleBatchBuilder.Build(SheetIds, spawnPlan, EmptyPlan("Warptiles"));
        var range = partial.Requests.Single().DeleteDimension.Range;
        Assert.Equal(7, range.SheetId);
        Assert.Equal(2, range.StartIndex);
    }

    private static ReplacementPlan EmptyPlan(string sheet)
        => new(sheet, Array.Empty<RowDelete>(), Array.Empty<RowInsert>());
}
