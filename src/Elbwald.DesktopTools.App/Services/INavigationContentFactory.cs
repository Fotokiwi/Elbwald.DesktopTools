using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.App.Services;

public interface INavigationContentFactory
{
    object CreateHomePage();

    object CreateSectionPage(string sectionId);

    object CreateAboutPage();

    object CreateDiagnosticsPage();

    object CreateOperationHistoryPage();

    object CreateLibraryHealthPage();

    object CreateDuplicateFinderPage();

    object CreateMediaIndexPage();

    object CreateProjectsPage();

    object CreateMediaImportPage();

    object CreateMetadataEditorPage();

    object CreateMediaExportPage();

    object CreateRenamePage();

    object CreateModulePage(ToolModuleDescriptor descriptor);
}
