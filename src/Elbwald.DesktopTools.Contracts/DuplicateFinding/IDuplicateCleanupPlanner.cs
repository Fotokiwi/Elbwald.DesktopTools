namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public interface IDuplicateCleanupPlanner
{
    Task<DuplicateCleanupPreflightReport> RunPreflightAsync(
        DuplicateCleanupPlan plan,
        CancellationToken cancellationToken = default);
}
