using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.App.Services;

public interface INavigationContentFactory
{
    object CreateHomePage();

    object CreateSectionPage(string sectionId);

    object CreateAboutPage();

    object CreateModulePage(ToolModuleDescriptor descriptor);
}
