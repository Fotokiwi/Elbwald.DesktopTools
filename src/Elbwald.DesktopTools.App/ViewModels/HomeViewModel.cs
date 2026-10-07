using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Core.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class HomeViewModel
{
    public HomeViewModel(
        IModuleRegistry moduleRegistry,
        IModuleLoadReport moduleLoadReport,
        INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(moduleLoadReport);
        ArgumentNullException.ThrowIfNull(navigationService);

        Modules = moduleRegistry.Modules
            .Select(module => new HomeModuleItemViewModel(
                module.GetDescriptor(),
                navigationService))
            .OrderBy(module => module.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        ModuleSummary = Modules.Count switch
        {
            0 => "Noch keine Werkzeuge geladen.",
            1 => "1 Werkzeug ist bereit.",
            _ => $"{Modules.Count} Werkzeuge sind bereit."
        };

        FailedModules = moduleLoadReport.Failures
            .Select(result => new ModuleLoadFailureViewModel(result))
            .ToArray();
    }

    public string Title => "Willkommen";

    public string ModuleSummary { get; }

    public IReadOnlyList<HomeModuleItemViewModel> Modules { get; }

    public IReadOnlyList<ModuleLoadFailureViewModel> FailedModules { get; }

    public bool HasModuleLoadFailures => FailedModules.Count > 0;
}
