using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class NavigationItemViewModel
{
    private NavigationItemViewModel(
        string id,
        string name,
        string description,
        string icon,
        NavigationPageKind pageKind,
        ToolModuleDescriptor? module,
        Action<string> activate,
        bool showSeparatorBefore)
    {
        Id = id;
        Name = name;
        Description = description;
        Icon = icon;
        PageKind = pageKind;
        Module = module;
        ShowSeparatorBefore = showSeparatorBefore;
        ActivateCommand = new RelayCommand(() => activate(Id));
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string Icon { get; }

    public NavigationPageKind PageKind { get; }

    public ToolModuleDescriptor? Module { get; }

    public bool ShowSeparatorBefore { get; }

    public IRelayCommand ActivateCommand { get; }

    public bool IsHome => Id == "home";

    public bool IsLibrary => Id == "library";

    public bool IsProjects => Id == "projects";

    public bool IsAnalyze => Id == "analyze";

    public bool IsOrganize => Id == "organize";

    public bool IsEdit => Id == "edit";

    public bool IsBackup => Id == "backup";

    public bool IsTools => Id == "tools";

    public bool IsSettings => Id == "settings";

    public static NavigationItemViewModel CreateHome(Action<string> activate)
    {
        return new NavigationItemViewModel(
            id: "home",
            name: "Start",
            description: "Übersicht und Schnellzugriff",
            icon: "Home",
            pageKind: NavigationPageKind.Home,
            module: null,
            activate,
            showSeparatorBefore: false);
    }

    public static NavigationItemViewModel CreateSection(
        string id,
        string name,
        string description,
        string icon,
        Action<string> activate,
        bool showSeparatorBefore = false)
    {
        return new NavigationItemViewModel(
            id,
            name,
            description,
            icon,
            NavigationPageKind.Section,
            module: null,
            activate,
            showSeparatorBefore);
    }

    public static NavigationItemViewModel CreateModuleRoute(
        string id,
        string name,
        string description,
        string icon,
        ToolModuleDescriptor module,
        Action<string> activate,
        bool showSeparatorBefore = false)
    {
        ArgumentNullException.ThrowIfNull(module);

        return new NavigationItemViewModel(
            id,
            name,
            description,
            icon,
            NavigationPageKind.Module,
            module,
            activate,
            showSeparatorBefore);
    }
}
