using System.Collections.Generic;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Validation;

namespace MapEditor.GameData.Sync;

public enum PushConflictChoice
{
    Overwrite,
    PullInstead,
    Cancel
}

public abstract record SyncResult;

public sealed record MapCatalogResult(IReadOnlyList<MapReference> Maps) : SyncResult;

public sealed record PullSucceededResult(GameDataSyncSession Session) : SyncResult;

public sealed record DirtyLocalRejectedResult() : SyncResult;

public sealed record ValidationRejectedResult(ValidationResult Validation) : SyncResult;

public sealed record PushConflictResult(RemoteOwnedRows LatestRemoteRows) : SyncResult;

public sealed record CancelledResult() : SyncResult;

public sealed record PullInsteadRequestedResult() : SyncResult;

public sealed record PushedResult() : SyncResult;

public sealed record AmbiguousResult() : SyncResult;
