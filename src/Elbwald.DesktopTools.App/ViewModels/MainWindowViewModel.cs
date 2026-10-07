using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Core.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationContentFactory _contentFactory;
    private readonly Dictionary<string, object> _pageCache = new(StringComparer.OrdinalIgnoreCase);

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

        var navigationItems = new List<NavigationItemViewModel>
        {
            NavigationItemViewModel.CreateHome()
        };

        navigationItems.AddRange(
            moduleRegistry.Modules
                .Select(module => module.GetDescriptor())
                .OrderBy(module => module.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(NavigationItemViewModel.CreateModule));

        NavigationItems = navigationItems;

        Greeting = NavigationItems.Count == 2
            ? "1 Modul geladen"
            : $"{NavigationItems.Count - 1} Module geladen";

        navigationService.NavigateRequested += NavigateTo;

        SelectedItem = NavigationItems[0];
    }

    public string ApplicationName => "Elbwald Digital";

    public string ApplicationSubtitle => "Desktop Tools";

    public string Greeting { get; }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    partial void OnSelectedItemChanged(NavigationItemViewModel? value)
    {
        if (value is null)
        {
            CurrentContent = null;
            return;
        }

        if (_pageCache.TryGetValue(value.Id, out var cachedPage))
        {
            CurrentContent = cachedPage;
            return;
        }

        var page = value.IsHome
            ? _contentFactory.CreateHomePage()
            : _contentFactory.CreateModulePage(
                value.Module
                ?? throw new InvalidOperationException(
                    $"Für den Navigationseintrag '{value.Id}' fehlt die Modulbeschreibung."));

        _pageCache[value.Id] = page;
        CurrentContent = page;
    }

    private void NavigateTo(string navigationId)
    {
        var target = NavigationItems.FirstOrDefault(
            item => string.Equals(
                item.Id,
                navigationId,
                StringComparison.OrdinalIgnoreCase));

        if (target is not null)
        {
            SelectedItem = target;
        }
    }
}
