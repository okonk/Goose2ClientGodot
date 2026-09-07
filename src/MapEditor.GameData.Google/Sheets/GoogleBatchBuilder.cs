using System;
using System.Collections.Generic;
using System.Linq;
using Google.Apis.Sheets.v4.Data;
using MapEditor.GameData.Replacement;

namespace MapEditor.GameData.Google.Sheets;

internal static class GoogleBatchBuilder
{
    public static BatchUpdateSpreadsheetRequest Build(
        IReadOnlyDictionary<string, int> sheetIds,
        ReplacementPlan spawnPlan,
        ReplacementPlan warpPlan)
    {
        var requests = new List<Request>();
        AppendPlan(requests, sheetIds, spawnPlan);
        AppendPlan(requests, sheetIds, warpPlan);
        return new BatchUpdateSpreadsheetRequest { Requests = requests };
    }

    private static void AppendPlan(
        List<Request> requests,
        IReadOnlyDictionary<string, int> sheetIds,
        ReplacementPlan plan)
    {
        if (plan.Deletes.Count == 0 && plan.Inserts.Count == 0)
        {
            return;
        }
        var sheetId = sheetIds[plan.Sheet];
        // Descending so deletes apply top-down without shifting the rows below.
        foreach (var delete in plan.Deletes.OrderByDescending(d => d.WorksheetRowNumber))
        {
            requests.Add(new Request
            {
                DeleteDimension = new DeleteDimensionRequest
                {
                    Range = new DimensionRange
                    {
                        SheetId = sheetId,
                        Dimension = "ROWS",
                        StartIndex = delete.WorksheetRowNumber - 1,
                        EndIndex = delete.WorksheetRowNumber
                    }
                }
            });
        }
        if (plan.Inserts.Count > 0)
        {
            var rows = new List<RowData>(plan.Inserts.Count);
            foreach (var insert in plan.Inserts)
            {
                var values = new List<CellData>();
                for (var i = 0; i < insert.CellValues.Count; i++)
                {
                    var cell = insert.CellValues[i];
                    if (string.IsNullOrEmpty(cell))
                    {
                        if (insert.CellValues.Skip(i + 1).Any(remaining => !string.IsNullOrEmpty(remaining)))
                        {
                            values.Add(new CellData());
                        }
                        continue;
                    }
                    values.Add(new CellData
                    {
                        UserEnteredValue = new ExtendedValue { StringValue = cell }
                    });
                }
                rows.Add(new RowData { Values = values });
            }
            requests.Add(new Request
            {
                AppendCells = new AppendCellsRequest
                {
                    SheetId = sheetId,
                    Rows = rows,
                    Fields = "userEnteredValue"
                }
            });
        }
    }
}
