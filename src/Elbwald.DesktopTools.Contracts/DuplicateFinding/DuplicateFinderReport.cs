namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public sealed record DuplicateFinderReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset? IndexLastUpdatedUtc,
    int PresentLocationCount,
    IReadOnlyList<ExactDuplicateGroup> Groups)
{
    public int DuplicateGroupCount => Groups.Count;

    public int DuplicateLocationCount => Groups.Sum(group => group.CopyCount);

    public int RedundantCopyCount => Groups.Sum(group => group.RedundantCopyCount);

    public long ReclaimableBytes => Groups.Sum(group => group.ReclaimableBytes);

    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
}
