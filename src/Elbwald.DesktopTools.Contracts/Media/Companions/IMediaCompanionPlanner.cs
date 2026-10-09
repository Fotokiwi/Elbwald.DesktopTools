namespace Elbwald.DesktopTools.Contracts.Media.Companions;

public interface IMediaCompanionPlanner
{
    IReadOnlyList<MediaCompanionGroupPlan> CreatePlans(
        string destinationRoot,
        IEnumerable<MediaCompanionContextEntry> entries,
        CancellationToken cancellationToken = default);
}
