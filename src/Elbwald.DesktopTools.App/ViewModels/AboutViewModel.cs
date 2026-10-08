using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class AboutViewModel
{
    public AboutViewModel(
        INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(navigationService);

        BackToStartCommand =
            new RelayCommand(
                () => navigationService.NavigateTo("home"));
    }

    public string ProductName =>
        "Elbwald Digital – Desktop Tools";

    public string Version =>
        MainWindowViewModel.PreviewVersion;

    public string BuildLabel =>
        "Preview Build";

    public string Description =>
        "Lokale Werkzeuge zum Analysieren, Organisieren, Bearbeiten und sicheren Verwalten von Medien.";

    public string SafetyPrinciple =>
        "Bei Unsicherheit wird nichts gelöscht und nichts überschrieben.";

    public IRelayCommand BackToStartCommand { get; }
}
