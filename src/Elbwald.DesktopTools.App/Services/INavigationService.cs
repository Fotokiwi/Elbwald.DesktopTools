using Elbwald.DesktopTools.Contracts.Navigation;

namespace Elbwald.DesktopTools.App.Services;

public interface INavigationService : IApplicationNavigation
{
    event Action<string>? NavigateRequested;
}
