namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class NavigationErrorViewModel
{
    public NavigationErrorViewModel(
        string moduleName,
        string technicalMessage)
    {
        ModuleName = string.IsNullOrWhiteSpace(moduleName)
            ? "Unbekanntes Werkzeug"
            : moduleName;

        TechnicalMessage = string.IsNullOrWhiteSpace(technicalMessage)
            ? "Keine weiteren technischen Details verfügbar."
            : technicalMessage;
    }

    public string ModuleName { get; }

    public string Title => "Werkzeug konnte nicht geöffnet werden";

    public string Message =>
        $"„{ModuleName}“ konnte nicht gestartet werden. "
        + "Die übrigen Desktop Tools können weiter verwendet werden.";

    public string TechnicalMessage { get; }
}
