namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public sealed record DuplicateCleanupPreflightReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    DuplicateCleanupPlan Plan,
    IReadOnlyList<DuplicateCleanupPreflightItem> Items,
    IReadOnlyList<string> GlobalIssues)
{
    public bool IsReady =>
        GlobalIssues.Count == 0
        && Items.Count == Plan.RemovalCount + 1
        && Items.All(item => item.IsReady);

    public int ReadyRemovalCount =>
        Items.Count(item => item.Action == DuplicateCleanupAction.Remove && item.IsReady);

    public long PlannedReclaimableBytes => Plan.PlannedReclaimableBytes;

    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
}
