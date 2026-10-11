using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Core.Modules;

public interface IModuleLoader
{
    IReadOnlyList<ModuleLoadResult> LoadModules(
        string modulesRoot,
        Version hostVersion,
        IServiceCollection services);
}
