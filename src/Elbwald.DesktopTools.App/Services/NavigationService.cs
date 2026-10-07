namespace Elbwald.DesktopTools.App.Services;

public sealed class NavigationService : INavigationService
{
    public event Action<string>? NavigateRequested;

    public void NavigateTo(string navigationId)
    {
        if (string.IsNullOrWhiteSpace(navigationId))
        {
            return;
        }

        NavigateRequested?.Invoke(navigationId);
    }
}
