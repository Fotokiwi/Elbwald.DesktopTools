using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Navigation;

public static class NavigationContributionServiceCollectionExtensions
{
    public static IServiceCollection AddNavigationContribution(
        this IServiceCollection services,
        NavigationContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentException.ThrowIfNullOrWhiteSpace(contribution.ExtensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contribution.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(contribution.RouteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contribution.Title);

        if (contribution.Placement == NavigationContributionPlacement.Section
            && string.IsNullOrWhiteSpace(contribution.SectionId))
        {
            throw new ArgumentException(
                "Section navigation contributions require a SectionId.",
                nameof(contribution));
        }

        services.AddSingleton(contribution);
        return services;
    }
}
