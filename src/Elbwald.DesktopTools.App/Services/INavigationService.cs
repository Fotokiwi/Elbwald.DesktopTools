namespace Elbwald.DesktopTools.App.Services;

public interface INavigationService
{
    event Action<string>? NavigateRequested;

    void NavigateTo(string navigationId);
}
