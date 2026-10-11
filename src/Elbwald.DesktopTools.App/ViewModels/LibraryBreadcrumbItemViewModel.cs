using CommunityToolkit.Mvvm.Input;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class LibraryBreadcrumbItemViewModel
{
    public LibraryBreadcrumbItemViewModel(
        string title,
        bool isCurrent,
        bool showSeparator,
        Func<Task> navigate)
    {
        Title = title;
        IsCurrent = isCurrent;
        ShowSeparator = showSeparator;
        NavigateCommand = new AsyncRelayCommand(navigate, () => !IsCurrent);
    }

    public string Title { get; }
    public bool IsCurrent { get; }
    public bool ShowSeparator { get; }
    public bool CanNavigate => !IsCurrent;
    public IAsyncRelayCommand NavigateCommand { get; }
}
