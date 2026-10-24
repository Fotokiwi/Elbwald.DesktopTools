namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public interface IDuplicateCleanupExecutor
{
    Task<DuplicateCleanupExecutionReport> ExecuteAsync(
        DuplicateCleanupPlan plan,
        CancellationToken cancellationToken = default);
}
