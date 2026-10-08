using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class SectionActionViewModel
{
    public SectionActionViewModel(
        string title,
        string description,
        string status,
        bool isAvailable,
        string? navigationId,
        INavigationService navigationService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentNullException.ThrowIfNull(navigationService);

        Title = title;
        Description = description;
        Status = status;
        IsAvailable = isAvailable;

        OpenCommand = new RelayCommand(
            () =>
            {
                if (!string.IsNullOrWhiteSpace(navigationId))
                {
                    navigationService.NavigateTo(navigationId);
                }
            },
            () =>
                isAvailable
                && !string.IsNullOrWhiteSpace(navigationId));
    }

    public string Title { get; }

    public string Description { get; }

    public string Status { get; }

    public bool IsAvailable { get; }

    public bool IsPlanned => !IsAvailable;

    public IRelayCommand OpenCommand { get; }
}
