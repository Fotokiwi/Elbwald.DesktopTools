using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Contracts.Navigation;

namespace Elbwald.DesktopTools.Core.Navigation;

public sealed class NavigationContributionRegistry : INavigationContributionRegistry
{
    private readonly NavigationContribution[] _contributions;
    private readonly IExtensionRegistry _extensionRegistry;

    public NavigationContributionRegistry(
        IEnumerable<NavigationContribution> contributions,
        IExtensionRegistry extensionRegistry)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(extensionRegistry);

        _extensionRegistry = extensionRegistry;
        _contributions = contributions
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<NavigationContribution> Contributions =>
        _contributions
            .Where(item => _extensionRegistry.IsEnabled(item.ExtensionId))
            .ToArray();

    public IReadOnlyList<NavigationContribution> GetPrimaryContributions() =>
        Contributions
            .Where(item => item.Placement == NavigationContributionPlacement.Primary)
            .ToArray();

    public IReadOnlyList<NavigationContribution> GetSectionContributions(string sectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);

        return Contributions
            .Where(
                item =>
                    item.Placement == NavigationContributionPlacement.Section
                    && string.Equals(
                        item.SectionId,
                        sectionId,
                        StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public NavigationContribution? FindByRoute(string routeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeId);

        return Contributions.FirstOrDefault(
            item => string.Equals(
                item.RouteId,
                routeId,
                StringComparison.OrdinalIgnoreCase));
    }
}
