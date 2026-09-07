using System.Collections.Generic;
using MapEditor.Core;
using MapEditor.GameData.Rows;

namespace MapEditor.App.Dialogs;

internal sealed record MapResizePlan(
    MapTileRectangle Window,
    int CroppedTiles,
    int CroppedSpawns,
    int CroppedWarps,
    int InboundWarps,
    bool HasPulledData,
    IReadOnlyList<NpcSpawnRow> NewSpawns,
    IReadOnlyList<WarpRow> NewWarps);
