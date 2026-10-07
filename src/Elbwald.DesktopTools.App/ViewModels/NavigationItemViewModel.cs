using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class NavigationItemViewModel
{
    private NavigationItemViewModel(
        string id,
        string name,
        string description,
        string icon,
        ToolModuleDescriptor? module)
    {
        Id = id;
        Name = name;
        Description = description;
        Icon = icon;
        Module = module;
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string Icon { get; }

    public ToolModuleDescriptor? Module { get; }

    public bool IsHome => Module is null;

    public bool IsImageTool =>
        string.Equals(Module?.Icon, "Image", StringComparison.OrdinalIgnoreCase);

    public bool IsMusicTool =>
        string.Equals(Module?.Icon, "Music", StringComparison.OrdinalIgnoreCase);

    public bool IsGenericTool =>
        !IsHome && !IsImageTool && !IsMusicTool;

    public static NavigationItemViewModel CreateHome()
    {
        return new NavigationItemViewModel(
            id: "home",
            name: "Start",
            description: "Übersicht und Schnellzugriff",
            icon: "Home",
            module: null);
    }

    public static NavigationItemViewModel CreateModule(
        ToolModuleDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new NavigationItemViewModel(
            id: descriptor.Id,
            name: descriptor.Name,
            description: descriptor.Description,
            icon: descriptor.Icon,
            module: descriptor);
    }
}
