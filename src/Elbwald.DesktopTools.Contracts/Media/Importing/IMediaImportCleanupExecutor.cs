namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public interface IMediaImportCleanupExecutor
{
    Task<MediaImportCleanupExecutionResult> ExecuteAsync(
        MediaImportCleanupPlan plan,
        bool removeEmptyDirectories,
        CancellationToken cancellationToken = default);
}
