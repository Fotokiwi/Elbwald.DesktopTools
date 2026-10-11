using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Contracts.Modules;

/// <summary>
/// Base contract for every dynamically loaded Elbwald extension.
/// Extensions are discovered before the application's service provider is built
/// and can contribute their own services during startup.
/// </summary>
public interface IExtensionModule
{
    string Id { get; }

    /// <summary>
    /// Registers services contributed by this extension.
    /// Implementations must only register services here and must not resolve
    /// services from a provider because the provider does not exist yet.
    /// </summary>
    void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }
}
