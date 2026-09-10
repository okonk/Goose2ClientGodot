using MapEditor.App.Rendering;
using MapEditor.Core.Terrain;

namespace MapEditor.App.Terrain;

internal enum TerrainCatalogSaveStatus
{
    Succeeded,
    NoChanges,
    InvalidDraft,
    SourceContextChanged,
    ReplacementInProgress,
    GestureCancellationFailed,
    WriteFailed
}

internal sealed record TerrainCatalogSaveResult
{
    internal TerrainCatalogSaveResult(
        TerrainCatalogSaveStatus status,
        IEnumerable<TerrainValidationIssue> issues,
        Exception? failure,
        IEnumerable<TerrainOperationWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(warnings);
        TerrainValidationIssue[] copiedIssues = issues.ToArray();
        TerrainOperationWarning[] copiedWarnings = warnings.ToArray();
        bool valid = status switch
        {
            TerrainCatalogSaveStatus.Succeeded => copiedIssues.Length == 0 && failure is null,
            TerrainCatalogSaveStatus.NoChanges => copiedIssues.Length == 0 && failure is null && copiedWarnings.Length == 0,
            TerrainCatalogSaveStatus.InvalidDraft => copiedIssues.Length > 0 && failure is null && copiedWarnings.Length == 0,
            TerrainCatalogSaveStatus.SourceContextChanged => copiedIssues.Length == 0 && failure is InvalidOperationException
                { Message: "The asset context changed while Terrain Sets was open. Discard this draft and reopen Terrain Sets against the current assets." } && copiedWarnings.Length == 0,
            TerrainCatalogSaveStatus.ReplacementInProgress => copiedIssues.Length == 0 && failure is InvalidOperationException
                { Message: "An asset replacement is already in progress." } && copiedWarnings.Length == 0,
            TerrainCatalogSaveStatus.GestureCancellationFailed => copiedIssues.Length == 0 && failure is AggregateException aggregate &&
                aggregate.InnerExceptions.Count > 0 && copiedWarnings.Length == 0,
            TerrainCatalogSaveStatus.WriteFailed => copiedIssues.Length == 0 && failure is not null && copiedWarnings.Length == 0,
            _ => false
        };
        if (!valid)
            throw new ArgumentException("The save result does not match its status.", nameof(status));

        Status = status;
        Issues = Array.AsReadOnly(copiedIssues);
        Failure = failure;
        Warnings = Array.AsReadOnly(copiedWarnings);
    }

    public TerrainCatalogSaveStatus Status { get; }
    public IReadOnlyList<TerrainValidationIssue> Issues { get; }
    public Exception? Failure { get; }
    public IReadOnlyList<TerrainOperationWarning> Warnings { get; }
    public bool ClosesDialog => Status is TerrainCatalogSaveStatus.Succeeded or TerrainCatalogSaveStatus.NoChanges;
}
