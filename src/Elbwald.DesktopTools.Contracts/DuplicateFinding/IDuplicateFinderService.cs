namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public interface IDuplicateFinderService
{
    Task<DuplicateFinderReport> FindExactDuplicatesAsync(
        CancellationToken cancellationToken = default);
}
