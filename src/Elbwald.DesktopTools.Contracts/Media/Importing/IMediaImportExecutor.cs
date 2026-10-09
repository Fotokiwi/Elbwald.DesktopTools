namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public interface IMediaImportExecutor
{
    Task<MediaImportExecutionResult> ExecuteAsync(
        MediaImportPlan plan,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
