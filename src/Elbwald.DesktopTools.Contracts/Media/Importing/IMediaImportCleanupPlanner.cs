namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public interface IMediaImportCleanupPlanner
{
    Task<MediaImportCleanupPlan> CreatePlanAsync(
        MediaImportPlan importPlan,
        CancellationToken cancellationToken = default);
}
