using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Core.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public const string PreviewVersion = "0.1.0-alpha.1";

    private const string MediaAnalyzerModuleId = "elbwald.mediaanalyzer";
    private const string PhotoSortModuleId = "elbwald.photosort";

    private readonly INavigationContentFactory _contentFactory;
    private readonly Dictionary<string, object> _pageCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyDictionary<string, ToolModuleDescriptor> _modules;

    [ObservableProperty]
    private NavigationItemViewModel? selectedItem;

    [ObservableProperty]
    private object? currentContent;

    public MainWindowViewModel(
        IModuleRegistry moduleRegistry,
        INavigationContentFactory contentFactory,
        INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(contentFactory);
        ArgumentNullException.ThrowIfNull(navigationService);

        _contentFactory = contentFactory;

        _modules = moduleRegistry.Modules
            .Select(module => module.GetDescriptor())
            .GroupBy(
                descriptor => descriptor.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        NavigationItems = CreateNavigationItems();

        NavigateHomeCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("home"));

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

        OpenAboutCommand =
            new RelayCommand(
                OpenAboutPage);

        ExitCommand =
            new RelayCommand(
                ExitApplication);

        navigationService.NavigateRequested += NavigateTo;

        SelectedItem = NavigationItems[0];
    }

    public string ApplicationName => "Elbwald Digital";

    public string ApplicationSubtitle => "Desktop Tools";

    public string ApplicationVersion => PreviewVersion;

    public string PrivacySummary => "Lokal verarbeitet · Keine Cloud";

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public IRelayCommand NavigateHomeCommand { get; }

    public IRelayCommand NavigateAnalyzeCommand { get; }

    public IRelayCommand NavigateOrganizeCommand { get; }

    public IRelayCommand NavigateEditCommand { get; }

    public IRelayCommand NavigateBackupCommand { get; }

    public IRelayCommand NavigateToolsCommand { get; }

    public IRelayCommand NavigateSettingsCommand { get; }

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

    private IReadOnlyList<NavigationItemViewModel> CreateNavigationItems()
    {
        var analyze = _modules.TryGetValue(
            MediaAnalyzerModuleId,
            out var analyzerModule)
            ? NavigationItemViewModel.CreateModuleRoute(
                "analyze",
                "Analysieren",
                "Bibliothek read-only prüfen",
                "Search",
                analyzerModule)
            : NavigationItemViewModel.CreateSection(
                "analyze",
                "Analysieren",
                "Bibliothek read-only prüfen",
                "Search");

        return new[]
        {
            NavigationItemViewModel.CreateHome(),
            analyze,
            NavigationItemViewModel.CreateSection(
                "organize",
                "Organisieren",
                "Importieren, sortieren und umbenennen",
                "Folder"),
            NavigationItemViewModel.CreateSection(
                "edit",
                "Bearbeiten",
                "Bilder, Audio und Video bearbeiten",
                "Edit"),
            NavigationItemViewModel.CreateSection(
                "backup",
                "Sichern",
                "Redundante Sicherungen prüfen und erstellen",
                "Backup"),
            NavigationItemViewModel.CreateSection(
                "tools",
                "Werkzeuge",
                "Metadaten und Konvertierung",
                "Tools"),
            NavigationItemViewModel.CreateSection(
                "settings",
                "Einstellungen",
                "Cache, Performance und Darstellung",
                "Settings")
        };
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
            SelectedItem = primaryTarget;
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

    private static string GetPrimaryOwnerId(
        string moduleId)
    {
        if (string.Equals(
                moduleId,
                MediaAnalyzerModuleId,
                StringComparison.OrdinalIgnoreCase))
        {
            return "analyze";
        }

        if (string.Equals(
                moduleId,
                PhotoSortModuleId,
                StringComparison.OrdinalIgnoreCase))
        {
            return "organize";
        }

        return "tools";
    }
}
