namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public sealed class MediaSortLiveExecutionResult
{
    public MediaSortLiveExecutionResult(
        MediaSortLiveExecutionState state,
        int completedOperationCount,
        int totalOperationCount,
        int completedGroupCount,
        int totalGroupCount,
        IEnumerable<MediaSortExecutionIssue>? issues = null,
        string? errorMessage = null,
        int skippedOperationCount = 0,
        int skippedGroupCount = 0,
        IEnumerable<MediaSortExecutionProblem>? problems = null)
    {
        if (completedOperationCount < 0
            || totalOperationCount < 0
            || completedGroupCount < 0
            || totalGroupCount < 0
            || skippedOperationCount < 0
            || skippedGroupCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedOperationCount));
        }

        if (completedOperationCount + skippedOperationCount
                > totalOperationCount
            || completedGroupCount + skippedGroupCount
                > totalGroupCount)
        {
            throw new ArgumentException(
                "Erfolgreiche und übersprungene Einträge dürfen die Gesamtanzahl nicht überschreiten.");
        }

        State = state;
        CompletedOperationCount = completedOperationCount;
        TotalOperationCount = totalOperationCount;
        CompletedGroupCount = completedGroupCount;
        TotalGroupCount = totalGroupCount;
        Issues = issues?.ToArray()
            ?? Array.Empty<MediaSortExecutionIssue>();
        ErrorMessage = errorMessage;
        SkippedOperationCount = skippedOperationCount;
        SkippedGroupCount = skippedGroupCount;
        Problems = problems?.ToArray()
            ?? Array.Empty<MediaSortExecutionProblem>();
    }

    public MediaSortLiveExecutionState State { get; }

    public int CompletedOperationCount { get; }

    public int TotalOperationCount { get; }

    public int CompletedGroupCount { get; }

    public int TotalGroupCount { get; }

    public IReadOnlyList<MediaSortExecutionIssue> Issues { get; }

    public string? ErrorMessage { get; }

    public int SkippedOperationCount { get; }

    public int SkippedGroupCount { get; }

    public IReadOnlyList<MediaSortExecutionProblem> Problems { get; }

    public bool IsSuccessful =>
        State is MediaSortLiveExecutionState.Completed
            or MediaSortLiveExecutionState.CompletedWithIssues;

    public bool RequiresRecovery =>
        State == MediaSortLiveExecutionState.RecoveryRequired;
}
