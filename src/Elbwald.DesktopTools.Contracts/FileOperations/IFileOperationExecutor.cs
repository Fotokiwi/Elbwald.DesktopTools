namespace Elbwald.DesktopTools.Contracts.FileOperations;

public interface IFileOperationExecutor
{
    Task<FileOperationExecutionResult> ExecuteAsync(
        FileOperationPlan plan,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
