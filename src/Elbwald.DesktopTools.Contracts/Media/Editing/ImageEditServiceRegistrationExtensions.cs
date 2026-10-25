using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Media.Editing;

public static class ImageEditServiceRegistrationExtensions
{
    public static IServiceCollection AddImageEditOperationProcessor<TImplementation>(
        this IServiceCollection services,
        string extensionId,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : class, IImageEditOperationProcessor
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        return services.AddExtensionCapabilityProvider<IImageEditOperationProcessor, TImplementation>(
            extensionId,
            ImageEditCapabilityIds.OperationProcessor,
            lifetime);
    }

    public static IServiceCollection AddImageEditRenderer<TImplementation>(
        this IServiceCollection services,
        string extensionId,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : class, IImageEditRenderer
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        return services.AddExtensionCapabilityProvider<IImageEditRenderer, TImplementation>(
            extensionId,
            ImageEditCapabilityIds.Renderer,
            lifetime);
    }

    public static IServiceCollection AddImageEditPreviewService<TImplementation>(
        this IServiceCollection services,
        string extensionId,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : class, IImageEditPreviewService
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        return services.AddExtensionCapabilityProvider<IImageEditPreviewService, TImplementation>(
            extensionId,
            ImageEditCapabilityIds.PreviewService,
            lifetime);
    }
}
