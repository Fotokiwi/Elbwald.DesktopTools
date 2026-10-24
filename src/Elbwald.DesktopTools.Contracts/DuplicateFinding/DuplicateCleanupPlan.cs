namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public sealed record DuplicateCleanupPlan(
    string MediaItemId,
    string ExpectedSha256,
    long ExpectedLength,
    long KeeperLocationId,
    IReadOnlyList<long> RemovalLocationIds)
{
    public int RemovalCount => RemovalLocationIds.Count;

    public long PlannedReclaimableBytes =>
        ExpectedLength <= 0 || RemovalCount == 0
            ? 0
            : ExpectedLength * RemovalCount;
}
