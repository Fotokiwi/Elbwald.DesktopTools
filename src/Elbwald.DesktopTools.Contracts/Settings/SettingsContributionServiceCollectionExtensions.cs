using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Settings;

public static class SettingsContributionServiceCollectionExtensions
{
    public static IServiceCollection AddSettingsContribution(
        this IServiceCollection services,
        SettingsContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(contribution);
        Validate(contribution);
        services.AddSingleton(contribution);
        return services;
    }

    public static IServiceCollection AddSettingsContribution<TViewModel, TView>(
        this IServiceCollection services,
        string extensionId,
        string id,
        string title,
        string description,
        string icon,
        int order)
        where TViewModel : class
        where TView : class
    {
        return services.AddSettingsContribution(
            new SettingsContribution(
                extensionId,
                id,
                title,
                description,
                icon,
                order,
                typeof(TViewModel),
                typeof(TView)));
    }

    private static void Validate(SettingsContribution contribution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contribution.ExtensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contribution.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(contribution.Title);

        if ((contribution.ViewModelType is null) != (contribution.ViewType is null))
        {
            throw new ArgumentException(
                "Settings contributions must specify both ViewModelType and ViewType or neither.",
                nameof(contribution));
        }
    }
}
