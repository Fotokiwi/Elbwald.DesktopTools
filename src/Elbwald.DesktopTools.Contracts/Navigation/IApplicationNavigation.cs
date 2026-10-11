namespace Elbwald.DesktopTools.Contracts.Navigation;

/// <summary>
/// Minimal application-level navigation contract that can be used by feature modules
/// without referencing the desktop shell project.
/// </summary>
public interface IApplicationNavigation
{
    void NavigateTo(string navigationId);
}
