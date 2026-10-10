using CommunityToolkit.Mvvm.Input;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class LibraryBreadcrumbItemViewModel
{
    public LibraryBreadcrumbItemViewModel(
        string title,
        string? endpointId,
        string? relativeFolder,
        bool isCurrent,
        bool showSeparator,
        Func<LibraryBreadcrumbItemViewModel, Task> navigate)
    {
        Title = title;
        EndpointId = endpointId;
        RelativeFolder = relativeFolder;
        IsCurrent = isCurrent;
        ShowSeparator = showSeparator;
        NavigateCommand = new AsyncRelayCommand(() => navigate(this), () => !IsCurrent);
    }

    public string Title { get; }
    public string? EndpointId { get; }
    public string? RelativeFolder { get; }
    public bool IsCurrent { get; }
    public bool ShowSeparator { get; }
    public bool CanNavigate => !IsCurrent;
    public IAsyncRelayCommand NavigateCommand { get; }
}
