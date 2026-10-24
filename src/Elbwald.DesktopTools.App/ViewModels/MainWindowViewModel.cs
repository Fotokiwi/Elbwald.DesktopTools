using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Contracts.Navigation;
using Elbwald.DesktopTools.Core.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public const string PreviewVersion = "0.1.0-alpha.1";

    private readonly INavigationContentFactory _contentFactory;
    private readonly INavigationService _navigationService;
    private readonly IModuleRegistry _moduleRegistry;
    private readonly IExtensionRegistry _extensionRegistry;
    private readonly INavigationContributionRegistry _navigationContributionRegistry;
    private readonly Dictionary<string, object> _pageCache =
        new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, ToolModuleDescriptor> _modules =
        new Dictionary<string, ToolModuleDescriptor>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<NavigationItemViewModel> _navigationItems = [];

    [ObservableProperty]
    private NavigationItemViewModel? selectedItem;

    [ObservableProperty]
    private object? currentContent;

    public MainWindowViewModel(
        IModuleRegistry moduleRegistry,
        IExtensionRegistry extensionRegistry,
        INavigationContributionRegistry navigationContributionRegistry,
        INavigationContentFactory contentFactory,
        INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(extensionRegistry);
        ArgumentNullException.ThrowIfNull(navigationContributionRegistry);
        ArgumentNullException.ThrowIfNull(contentFactory);
        ArgumentNullException.ThrowIfNull(navigationService);

        _moduleRegistry = moduleRegistry;
        _extensionRegistry = extensionRegistry;
        _navigationContributionRegistry = navigationContributionRegistry;
        _contentFactory = contentFactory;
        _navigationService = navigationService;

        RefreshActiveModules();
        NavigationItems = CreateNavigationItems(navigationService.NavigateTo);

        NavigateHomeCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("home"));

        NavigateLibraryCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("library"));

        NavigateAnalyzeCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("analyze"));

        NavigateOrganizeCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("organize"));

        NavigateEditCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("edit"));

        NavigateBackupCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("backup"));

        NavigateToolsCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("tools"));

        NavigateSettingsCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("settings"));

        NavigateDiagnosticsCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("diagnostics"));

        NavigateLibraryHealthCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("library-health"));

        NavigateDuplicateFinderCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("duplicate-finder"));

        NavigateMediaIndexCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("media-index"));

        NavigateProjectsCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("projects"));

        NavigateImportCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("import"));

        NavigateMetadataCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("metadata"));

        NavigateRenameCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("rename"));

        OpenSupportersCommand =
            new RelayCommand(
                OpenSupportersPage);

        OpenAboutCommand =
            new RelayCommand(
                OpenAboutPage);

        ExitCommand =
            new RelayCommand(
                ExitApplication);

        navigationService.NavigateRequested += NavigateTo;
        _extensionRegistry.ActivationChanged += OnExtensionActivationChanged;

        SelectedItem = NavigationItems[0];
    }

    public string ApplicationName => "Elbwald Digital";

    public string ApplicationSubtitle => "Desktop Tools";

    public string ApplicationVersion => PreviewVersion;

    public string PrivacySummary => "Lokal verarbeitet · Keine Cloud";

    public IReadOnlyList<NavigationItemViewModel> NavigationItems
    {
        get => _navigationItems;
        private set => SetProperty(ref _navigationItems, value);
    }

    public IRelayCommand NavigateHomeCommand { get; }

    public IRelayCommand NavigateLibraryCommand { get; }

    public IRelayCommand NavigateAnalyzeCommand { get; }

    public IRelayCommand NavigateOrganizeCommand { get; }

    public IRelayCommand NavigateEditCommand { get; }

    public IRelayCommand NavigateBackupCommand { get; }

    public IRelayCommand NavigateToolsCommand { get; }

    public IRelayCommand NavigateSettingsCommand { get; }

    public IRelayCommand NavigateDiagnosticsCommand { get; }

    public IRelayCommand NavigateLibraryHealthCommand { get; }

    public IRelayCommand NavigateDuplicateFinderCommand { get; }

    public IRelayCommand NavigateMediaIndexCommand { get; }

    public IRelayCommand NavigateProjectsCommand { get; }

    public IRelayCommand NavigateImportCommand { get; }

    public IRelayCommand NavigateMetadataCommand { get; }

    public IRelayCommand NavigateRenameCommand { get; }

    public IRelayCommand OpenSupportersCommand { get; }

    public IRelayCommand OpenAboutCommand { get; }

    public IRelayCommand ExitCommand { get; }

    partial void OnSelectedItemChanged(NavigationItemViewModel? value)
    {
        if (value is null)
        {
            CurrentContent = null;
            return;
        }

        CurrentContent = GetOrCreatePage(value);
    }

    private void OnExtensionActivationChanged(
        object? sender,
        ExtensionActivationChangedEventArgs args)
    {
        var selectedId = SelectedItem?.Id;

        RefreshActiveModules();
        NavigationItems = CreateNavigationItems(_navigationService.NavigateTo);

        // Contributions can change primary and section navigation immediately.
        // Keep the currently displayed settings page alive, but invalidate cached
        // pages so the next navigation reflects the new contribution set.
        _pageCache.Clear();

        if (selectedId is null)
        {
            return;
        }

        var replacement = NavigationItems.FirstOrDefault(
            item => string.Equals(
                item.Id,
                selectedId,
                StringComparison.OrdinalIgnoreCase));

        if (replacement is null)
        {
            return;
        }

        // Keep the current content alive while the settings page toggles an
        // extension. Replacing SelectedItem through its property would recreate
        // the current settings page in the middle of the click.
        selectedItem = replacement;
        OnPropertyChanged(nameof(SelectedItem));
    }

    private void RefreshActiveModules()
    {
        _modules = _moduleRegistry.Modules
            .Select(module => module.GetDescriptor())
            .GroupBy(
                descriptor => descriptor.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<NavigationItemViewModel> CreateNavigationItems(Action<string> activate)
    {
        var items = new List<(int Order, NavigationItemViewModel Item)>
        {
            (0, NavigationItemViewModel.CreateHome(activate)),
            (100, NavigationItemViewModel.CreateSection(
                "library",
                "Bibliothek",
                "Medien durchsuchen und auswählen",
                "Camera",
                activate,
                showSeparatorBefore: true)),
            (200, NavigationItemViewModel.CreateSection(
                "projects",
                "Projekte",
                "Medien zu Vorhaben und Sammlungen zusammenstellen",
                "Project",
                activate)),
            (300, NavigationItemViewModel.CreateSection(
                "analyze",
                "Analysieren",
                "Bibliothek read-only prüfen",
                "Search",
                activate,
                showSeparatorBefore: true)),
            (400, NavigationItemViewModel.CreateSection(
                "organize",
                "Organisieren",
                "Importieren, sortieren und umbenennen",
                "Folder",
                activate)),
            (500, NavigationItemViewModel.CreateSection(
                "edit",
                "Bearbeiten",
                "Bilder, Audio und Video bearbeiten",
                "Edit",
                activate)),
            (600, NavigationItemViewModel.CreateSection(
                "backup",
                "Sichern",
                "Redundante Sicherungen prüfen und erstellen",
                "Backup",
                activate,
                showSeparatorBefore: true)),
            (700, NavigationItemViewModel.CreateSection(
                "tools",
                "Werkzeuge",
                "Metadaten und Konvertierung",
                "Tools",
                activate)),
            (800, NavigationItemViewModel.CreateSection(
                "settings",
                "Einstellungen",
                "Cache, Performance und Darstellung",
                "Settings",
                activate,
                showSeparatorBefore: true))
        };

        foreach (var contribution in _navigationContributionRegistry.GetPrimaryContributions())
        {
            NavigationItemViewModel? contributionItem = null;

            if (!string.IsNullOrWhiteSpace(contribution.ToolModuleId)
                && _modules.TryGetValue(contribution.ToolModuleId, out var module))
            {
                contributionItem = NavigationItemViewModel.CreateModuleRoute(
                    contribution.Id,
                    contribution.Title,
                    contribution.Description,
                    contribution.Icon,
                    module,
                    activate,
                    contribution.ShowSeparatorBefore);
            }
            else if (string.Equals(
                         contribution.Id,
                         contribution.RouteId,
                         StringComparison.OrdinalIgnoreCase))
            {
                contributionItem = NavigationItemViewModel.CreateSection(
                    contribution.Id,
                    contribution.Title,
                    contribution.Description,
                    contribution.Icon,
                    activate,
                    contribution.ShowSeparatorBefore);
            }

            if (contributionItem is null)
            {
                continue;
            }

            items.RemoveAll(
                entry => string.Equals(
                    entry.Item.Id,
                    contribution.Id,
                    StringComparison.OrdinalIgnoreCase));

            items.Add((contribution.Order, contributionItem));
        }

        return items
            .OrderBy(entry => entry.Order)
            .ThenBy(entry => entry.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => entry.Item)
            .ToArray();
    }

    private object GetOrCreatePage(
        NavigationItemViewModel item)
    {
        var cacheKey =
            $"primary:{item.Id}";

        if (_pageCache.TryGetValue(
                cacheKey,
                out var cachedPage))
        {
            return cachedPage;
        }

        var page = item.PageKind switch
        {
            NavigationPageKind.Home =>
                _contentFactory.CreateHomePage(),

            NavigationPageKind.Module =>
                _contentFactory.CreateModulePage(
                    item.Module
                    ?? throw new InvalidOperationException(
                        $"Für '{item.Id}' fehlt die Modulbeschreibung.")),

            NavigationPageKind.Section =>
                _contentFactory.CreateSectionPage(
                    item.Id),

            _ =>
                throw new InvalidOperationException(
                    $"Unbekannter Navigationstyp für '{item.Id}'.")
        };

        _pageCache[cacheKey] = page;

        return page;
    }

    private void NavigateTo(string navigationId)
    {
        var primaryTarget = NavigationItems.FirstOrDefault(
            item => string.Equals(
                item.Id,
                navigationId,
                StringComparison.OrdinalIgnoreCase));

        if (primaryTarget is not null)
        {
            // A primary navigation item can already be selected while a child page
            // (for example Rename or Photo Sort) is currently displayed. In that
            // case assigning SelectedItem again does not raise the generated
            // change notification, so explicitly restore the section/root page.
            SelectedItem = primaryTarget;
            CurrentContent = GetOrCreatePage(primaryTarget);
            return;
        }

        if (string.Equals(
                navigationId,
                "import",
                StringComparison.OrdinalIgnoreCase))
        {
            var organize = NavigationItems.FirstOrDefault(
                item => string.Equals(item.Id, "organize", StringComparison.OrdinalIgnoreCase));

            if (organize is not null)
            {
                SelectedItem = organize;
            }

            const string cacheKey = "special:import";
            if (!_pageCache.TryGetValue(cacheKey, out var importPage))
            {
                importPage = _contentFactory.CreateMediaImportPage();
                _pageCache[cacheKey] = importPage;
            }

            CurrentContent = importPage;
            return;
        }

        if (string.Equals(
                navigationId,
                "metadata",
                StringComparison.OrdinalIgnoreCase))
        {
            var tools = NavigationItems.FirstOrDefault(
                item => string.Equals(item.Id, "tools", StringComparison.OrdinalIgnoreCase));

            if (tools is not null)
            {
                SelectedItem = tools;
            }

            const string cacheKey = "special:metadata";
            if (!_pageCache.TryGetValue(cacheKey, out var metadataPage))
            {
                metadataPage = _contentFactory.CreateMetadataEditorPage();
                _pageCache[cacheKey] = metadataPage;
            }

            CurrentContent = metadataPage;
            return;
        }

        if (string.Equals(
                navigationId,
                "export",
                StringComparison.OrdinalIgnoreCase))
        {
            // Export can be opened from the library or from the tools hub.
            // Keep the current primary navigation highlight. A fresh page is intentional
            // so the latest gallery selection/project state is reflected immediately.
            CurrentContent = _contentFactory.CreateMediaExportPage();
            return;
        }

        if (string.Equals(
                navigationId,
                "rename",
                StringComparison.OrdinalIgnoreCase))
        {
            var organize = NavigationItems.FirstOrDefault(
                item => string.Equals(item.Id, "organize", StringComparison.OrdinalIgnoreCase));

            if (organize is not null)
            {
                SelectedItem = organize;
            }

            const string cacheKey = "special:rename";
            if (!_pageCache.TryGetValue(cacheKey, out var renamePage))
            {
                renamePage = _contentFactory.CreateRenamePage();
                _pageCache[cacheKey] = renamePage;
            }

            CurrentContent = renamePage;
            return;
        }

        if (string.Equals(
                navigationId,
                "projects",
                StringComparison.OrdinalIgnoreCase))
        {
            var organize = NavigationItems.FirstOrDefault(
                item => string.Equals(item.Id, "organize", StringComparison.OrdinalIgnoreCase));

            if (organize is not null)
            {
                SelectedItem = organize;
            }

            const string cacheKey = "special:projects";
            if (!_pageCache.TryGetValue(cacheKey, out var projectsPage))
            {
                projectsPage = _contentFactory.CreateProjectsPage();
                _pageCache[cacheKey] = projectsPage;
            }

            CurrentContent = projectsPage;
            return;
        }

        if (string.Equals(
                navigationId,
                "media-index",
                StringComparison.OrdinalIgnoreCase))
        {
            var tools = NavigationItems.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    "tools",
                    StringComparison.OrdinalIgnoreCase));

            if (tools is not null)
            {
                SelectedItem = tools;
            }

            const string cacheKey =
                "special:media-index";

            if (!_pageCache.TryGetValue(
                    cacheKey,
                    out var mediaIndexPage))
            {
                mediaIndexPage =
                    _contentFactory.CreateMediaIndexPage();

                _pageCache[cacheKey] =
                    mediaIndexPage;
            }

            CurrentContent =
                mediaIndexPage;

            return;
        }

        if (string.Equals(
                navigationId,
                "duplicate-finder",
                StringComparison.OrdinalIgnoreCase))
        {
            var analyze = NavigationItems.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    "analyze",
                    StringComparison.OrdinalIgnoreCase));

            if (analyze is not null)
            {
                SelectedItem = analyze;
            }

            const string cacheKey =
                "special:duplicate-finder";

            if (!_pageCache.TryGetValue(
                    cacheKey,
                    out var duplicateFinderPage))
            {
                duplicateFinderPage =
                    _contentFactory.CreateDuplicateFinderPage();

                _pageCache[cacheKey] =
                    duplicateFinderPage;
            }

            CurrentContent =
                duplicateFinderPage;

            return;
        }

        if (string.Equals(
                navigationId,
                "library-health",
                StringComparison.OrdinalIgnoreCase))
        {
            var tools = NavigationItems.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    "tools",
                    StringComparison.OrdinalIgnoreCase));

            if (tools is not null)
            {
                SelectedItem = tools;
            }

            const string cacheKey =
                "special:library-health";

            if (!_pageCache.TryGetValue(
                    cacheKey,
                    out var libraryHealthPage))
            {
                libraryHealthPage =
                    _contentFactory.CreateLibraryHealthPage();

                _pageCache[cacheKey] =
                    libraryHealthPage;
            }

            CurrentContent =
                libraryHealthPage;

            return;
        }

        if (string.Equals(
                navigationId,
                "operation-history",
                StringComparison.OrdinalIgnoreCase))
        {
            var tools = NavigationItems.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    "tools",
                    StringComparison.OrdinalIgnoreCase));

            if (tools is not null)
            {
                SelectedItem = tools;
            }

            const string cacheKey =
                "special:operation-history";

            if (!_pageCache.TryGetValue(
                    cacheKey,
                    out var operationHistoryPage))
            {
                operationHistoryPage =
                    _contentFactory.CreateOperationHistoryPage();

                _pageCache[cacheKey] =
                    operationHistoryPage;
            }

            CurrentContent =
                operationHistoryPage;

            return;
        }

        if (string.Equals(
                navigationId,
                "diagnostics",
                StringComparison.OrdinalIgnoreCase))
        {
            var tools = NavigationItems.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    "tools",
                    StringComparison.OrdinalIgnoreCase));

            if (tools is not null)
            {
                SelectedItem = tools;
            }

            const string cacheKey =
                "special:diagnostics";

            if (!_pageCache.TryGetValue(
                    cacheKey,
                    out var diagnosticsPage))
            {
                diagnosticsPage =
                    _contentFactory.CreateDiagnosticsPage();

                _pageCache[cacheKey] =
                    diagnosticsPage;
            }

            CurrentContent =
                diagnosticsPage;

            return;
        }

        if (!_modules.TryGetValue(
                navigationId,
                out var module))
        {
            return;
        }

        var ownerId = GetPrimaryOwnerId(
            module.Id);

        var owner = NavigationItems.FirstOrDefault(
            item => string.Equals(
                item.Id,
                ownerId,
                StringComparison.OrdinalIgnoreCase));

        if (owner is not null)
        {
            SelectedItem = owner;
        }

        var moduleCacheKey =
            $"module:{module.Id}";

        if (!_pageCache.TryGetValue(
                moduleCacheKey,
                out var modulePage))
        {
            modulePage =
                _contentFactory.CreateModulePage(module);

            _pageCache[moduleCacheKey] =
                modulePage;
        }

        CurrentContent =
            modulePage;
    }

    private void OpenSupportersPage()
    {
        const string cacheKey =
            "special:supporters";

        if (!_pageCache.TryGetValue(
                cacheKey,
                out var page))
        {
            page =
                _contentFactory.CreateSectionPage("supporters");

            _pageCache[cacheKey] =
                page;
        }

        // Supporters is an informational help page, not a primary workspace.
        SelectedItem = null;
        CurrentContent = page;
    }

    private void OpenAboutPage()
    {
        const string cacheKey =
            "special:about";

        if (!_pageCache.TryGetValue(
                cacheKey,
                out var page))
        {
            page =
                _contentFactory.CreateAboutPage();

            _pageCache[cacheKey] =
                page;
        }

        SelectedItem = null;

        CurrentContent =
            page;
    }

    private static void ExitApplication()
    {
        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private string GetPrimaryOwnerId(string moduleId)
    {
        var contribution = _navigationContributionRegistry.FindByRoute(moduleId);

        if (contribution is null)
        {
            return "tools";
        }

        return contribution.Placement switch
        {
            NavigationContributionPlacement.Primary => contribution.Id,
            NavigationContributionPlacement.Section when !string.IsNullOrWhiteSpace(contribution.SectionId)
                => contribution.SectionId,
            _ => "tools"
        };
    }
}
