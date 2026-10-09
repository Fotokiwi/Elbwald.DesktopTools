namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public interface IMediaImportPlanner
{
    Task<MediaImportPlan> CreatePlanAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken = default);
}
