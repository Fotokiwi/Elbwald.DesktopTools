using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Media.Decoding;

public static class DecodeServiceRegistrationExtensions
{
    public static IServiceCollection AddImageDecodeProvider<TImplementation>(
        this IServiceCollection services,
        string extensionId,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : class, IImageDecodeProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        return services.AddExtensionCapabilityProvider<IImageDecodeProvider, TImplementation>(
            extensionId,
            DecodeCapabilityIds.ImageProvider,
            lifetime);
    }
}
