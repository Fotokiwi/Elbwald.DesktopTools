using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class HomeModuleItemViewModel
{
    public HomeModuleItemViewModel(
        ToolModuleDescriptor descriptor,
        INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(navigationService);

        Id = descriptor.Id;
        Name = descriptor.Name;
        Description = descriptor.Description;
        Icon = descriptor.Icon;

        OpenCommand = new RelayCommand(
            () => navigationService.NavigateTo(Id));
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string Icon { get; }

    public bool IsImageTool =>
        string.Equals(Icon, "Image", StringComparison.OrdinalIgnoreCase);

    public bool IsMusicTool =>
        string.Equals(Icon, "Music", StringComparison.OrdinalIgnoreCase);

    public bool IsGenericTool =>
        !IsImageTool && !IsMusicTool;

    public IRelayCommand OpenCommand { get; }
}
