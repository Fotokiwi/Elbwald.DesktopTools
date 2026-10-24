namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public sealed record DuplicateCleanupExecutionReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    DuplicateCleanupPlan Plan,
    IReadOnlyList<DuplicateCleanupExecutionItem> Items,
    IReadOnlyList<string> GlobalIssues,
    bool IndexUpdated,
    bool HistoryRecorded,
    string? HistoryBatchId)
{
    public int SucceededCount => Items.Count(item => item.Succeeded);

    public int FailedCount => Math.Max(0, Plan.RemovalCount - SucceededCount);

    public long MovedBytes => Items.Sum(item => item.MovedBytes);

    public bool IsSuccessful =>
        SucceededCount == Plan.RemovalCount
        && FailedCount == 0
        && IndexUpdated
        && HistoryRecorded
        && GlobalIssues.Count == 0;

    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
}
