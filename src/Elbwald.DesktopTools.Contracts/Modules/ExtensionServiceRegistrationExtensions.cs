using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Modules;

/// <summary>
/// Helpers used by extensions to register typed providers for manifest-declared
/// capabilities. Registration only describes availability; provider selection
/// remains the responsibility of the host or a specialized registry.
/// </summary>
public static class ExtensionServiceRegistrationExtensions
{
    public static IServiceCollection AddExtensionCapabilityProvider<TContract, TImplementation>(
        this IServiceCollection services,
        string extensionId,
        string capabilityId,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);

        var implementationType = typeof(TImplementation);

        if (!services.Any(descriptor => descriptor.ServiceType == implementationType))
        {
            services.Add(
                ServiceDescriptor.Describe(
                    implementationType,
                    implementationType,
                    lifetime));
        }

        services.AddSingleton(
            new ExtensionCapabilityProviderRegistration(
                extensionId.Trim(),
                capabilityId.Trim(),
                typeof(TContract),
                implementationType,
                lifetime));

        return services;
    }
}
