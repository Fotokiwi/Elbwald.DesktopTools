using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Media.Metadata;

/// <summary>
/// Convenience registration for extension-provided image metadata readers.
/// The extension manifest must declare
/// <see cref="MetadataCapabilityIds.ImageReaderProvider"/>.
/// </summary>
public static class MetadataServiceRegistrationExtensions
{
    public static IServiceCollection AddImageMetadataProvider<TImplementation>(
        this IServiceCollection services,
        string extensionId,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : class, IImageMetadataProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        return services.AddExtensionCapabilityProvider<IImageMetadataProvider, TImplementation>(
            extensionId,
            MetadataCapabilityIds.ImageReaderProvider,
            lifetime);
    }
}
