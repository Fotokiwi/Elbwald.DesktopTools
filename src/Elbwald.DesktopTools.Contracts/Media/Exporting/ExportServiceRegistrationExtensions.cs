using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Media.Exporting;

public static class ExportServiceRegistrationExtensions
{
    public static IServiceCollection AddImageExportProvider<TImplementation>(
        this IServiceCollection services,
        string extensionId,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : class, IImageExportProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        return services.AddExtensionCapabilityProvider<IImageExportProvider, TImplementation>(
            extensionId,
            ExportCapabilityIds.ImageExporterProvider,
            lifetime);
    }
}
