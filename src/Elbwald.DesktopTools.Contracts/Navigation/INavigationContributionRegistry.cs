namespace Elbwald.DesktopTools.Contracts.Navigation;

public interface INavigationContributionRegistry
{
    IReadOnlyList<NavigationContribution> Contributions { get; }

    IReadOnlyList<NavigationContribution> GetPrimaryContributions();

    IReadOnlyList<NavigationContribution> GetSectionContributions(string sectionId);

    NavigationContribution? FindByRoute(string routeId);
}
