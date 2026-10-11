using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Media.Formats;

/// <summary>
/// Convenience registration for extension-provided media format descriptors.
/// The extension manifest must declare <see cref="MediaCapabilityIds.FormatProvider"/>.
/// </summary>
public static class MediaFormatServiceRegistrationExtensions
{
    public static IServiceCollection AddMediaFormatProvider<TImplementation>(
        this IServiceCollection services,
        string extensionId,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : class, IMediaFormatProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        return services.AddExtensionCapabilityProvider<IMediaFormatProvider, TImplementation>(
            extensionId,
            MediaCapabilityIds.FormatProvider,
            lifetime);
    }
}
