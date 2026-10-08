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
        ToolModuleDescriptor? module)
    {
        Id = id;
        Name = name;
        Description = description;
        Icon = icon;
        PageKind = pageKind;
        Module = module;
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string Icon { get; }

    public NavigationPageKind PageKind { get; }

    public ToolModuleDescriptor? Module { get; }

    public bool IsHome => Id == "home";

    public bool IsAnalyze => Id == "analyze";

    public bool IsOrganize => Id == "organize";

    public bool IsEdit => Id == "edit";

    public bool IsBackup => Id == "backup";

    public bool IsTools => Id == "tools";

    public bool IsSettings => Id == "settings";

    public static NavigationItemViewModel CreateHome()
    {
        return new NavigationItemViewModel(
            id: "home",
            name: "Start",
            description: "Übersicht und Schnellzugriff",
            icon: "Home",
            pageKind: NavigationPageKind.Home,
            module: null);
    }

    public static NavigationItemViewModel CreateSection(
        string id,
        string name,
        string description,
        string icon)
    {
        return new NavigationItemViewModel(
            id,
            name,
            description,
            icon,
            NavigationPageKind.Section,
            module: null);
    }

    public static NavigationItemViewModel CreateModuleRoute(
        string id,
        string name,
        string description,
        string icon,
        ToolModuleDescriptor module)
    {
        ArgumentNullException.ThrowIfNull(module);

        return new NavigationItemViewModel(
            id,
            name,
            description,
            icon,
            NavigationPageKind.Module,
            module);
    }
}
