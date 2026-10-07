using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.App.Services;

public interface INavigationContentFactory
{
    object CreateHomePage();

    object CreateModulePage(ToolModuleDescriptor descriptor);
}
