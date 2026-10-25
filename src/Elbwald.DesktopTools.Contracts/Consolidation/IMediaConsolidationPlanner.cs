namespace Elbwald.DesktopTools.Contracts.Consolidation;

public interface IMediaConsolidationPlanner
{
    Task<MediaConsolidationPlan> PlanAsync(
        MediaConsolidationPlanRequest request,
        IProgress<MediaConsolidationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
