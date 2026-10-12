using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Media.Thumbnailing;

public static class ThumbnailServiceRegistrationExtensions
{
    public static IServiceCollection AddImageThumbnailProvider<TImplementation>(
        this IServiceCollection services,
        string extensionId,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : class, IImageThumbnailProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        return services.AddExtensionCapabilityProvider<IImageThumbnailProvider, TImplementation>(
            extensionId,
            ThumbnailCapabilityIds.ImageProvider,
            lifetime);
    }
}
