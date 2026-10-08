using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Core.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class SectionHubViewModel
{
    private const string PhotoSortModuleId = "elbwald.photosort";

    public SectionHubViewModel(
        string sectionId,
        IModuleRegistry moduleRegistry,
        INavigationService navigationService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(navigationService);

        var loadedModuleIds = moduleRegistry.Modules
            .Select(module => module.GetDescriptor().Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        (Title, Subtitle, Actions) = sectionId switch
        {
            "analyze" => CreateAnalyzeFallback(
                navigationService),

            "organize" => CreateOrganize(
                loadedModuleIds,
                navigationService),

            "edit" => CreateEdit(
                navigationService),

            "backup" => CreateBackup(
                navigationService),

            "tools" => CreateTools(
                navigationService),

            "settings" => CreateSettings(
                navigationService),

            _ => (
                "Bereich",
                "Dieser Bereich wird schrittweise ausgebaut.",
                Array.Empty<SectionActionViewModel>())
        };
    }

    public string Title { get; }

    public string Subtitle { get; }

    public IReadOnlyList<SectionActionViewModel> Actions { get; }

    private static (
        string Title,
        string Subtitle,
        IReadOnlyList<SectionActionViewModel> Actions) CreateAnalyzeFallback(
            INavigationService navigationService)
    {
        return (
            "Analysieren",
            "Der Analyzer ist der zentrale read-only Bereich der App.",
            new[]
            {
                new SectionActionViewModel(
                    "Media Analyzer",
                    "Das Analyzer-Modul konnte nicht geladen werden. Prüfe den Modulstatus auf der Startseite.",
                    "Modul nicht geladen",
                    isAvailable: false,
                    navigationId: null,
                    navigationService)
            });
    }

    private static (
        string Title,
        string Subtitle,
        IReadOnlyList<SectionActionViewModel> Actions) CreateOrganize(
            IReadOnlySet<string> loadedModuleIds,
            INavigationService navigationService)
    {
        var photoSortReady = loadedModuleIds.Contains(PhotoSortModuleId);

        return (
            "Organisieren",
            "Medien importieren, sicher strukturieren und einheitlich benennen.",
            new[]
            {
                Planned(
                    "Importieren",
                    "Kamera, Smartphone, SD-Karte oder Ordner sicher übernehmen.",
                    navigationService),

                new SectionActionViewModel(
                    "Sortieren",
                    "Fotos und später Videos nach nachvollziehbaren Regeln strukturieren.",
                    photoSortReady ? "Bereit" : "Modul nicht geladen",
                    photoSortReady,
                    photoSortReady ? PhotoSortModuleId : null,
                    navigationService),

                Planned(
                    "Umbenennen",
                    "Dateien anhand von Datum, Kamera und Metadaten einheitlich benennen.",
                    navigationService)
            });
    }

    private static (
        string Title,
        string Subtitle,
        IReadOnlyList<SectionActionViewModel> Actions) CreateEdit(
            INavigationService navigationService)
    {
        return (
            "Bearbeiten",
            "Alltagstaugliche Bearbeitung nach Medientyp – bewusst ohne überladene Profi-Oberfläche.",
            new[]
            {
                Planned(
                    "Bilder",
                    "Beschneiden, optimieren, Collagen und Stapelbearbeitung.",
                    navigationService),

                Planned(
                    "Audio",
                    "Tags, Cover und Albumdaten komfortabel bearbeiten.",
                    navigationService),

                Planned(
                    "Video",
                    "Spätere einfache Bearbeitungs- und Konvertierungsfunktionen.",
                    navigationService)
            });
    }

    private static (
        string Title,
        string Subtitle,
        IReadOnlyList<SectionActionViewModel> Actions) CreateBackup(
            INavigationService navigationService)
    {
        return (
            "Sichern",
            "Redundante, überprüfbare Sicherungen mit Integritätskontrolle statt Blackbox-Backup.",
            new[]
            {
                Planned(
                    "Sicherungsstatus",
                    "Prüfen, ob Kopien vollständig, lesbar und aktuell sind.",
                    navigationService),

                Planned(
                    "Sicherung erstellen",
                    "Medien sicher auf zusätzliche Datenträger oder Ziele kopieren.",
                    navigationService),

                Planned(
                    "Wiederherstellen",
                    "Sicherungen kontrolliert zurückspielen und anschließend verifizieren.",
                    navigationService)
            });
    }

    private static (
        string Title,
        string Subtitle,
        IReadOnlyList<SectionActionViewModel> Actions) CreateTools(
            INavigationService navigationService)
    {
        return (
            "Werkzeuge",
            "Kleinere medienübergreifende Funktionen, die keinen eigenen Hauptbereich benötigen.",
            new[]
            {
                Planned(
                    "Metadaten",
                    "Metadaten prüfen, korrigieren oder sensible Informationen entfernen.",
                    navigationService),

                Planned(
                    "Konvertieren",
                    "Bilder, Audio und später Video in geeignete Formate umwandeln.",
                    navigationService)
            });
    }

    private static (
        string Title,
        string Subtitle,
        IReadOnlyList<SectionActionViewModel> Actions) CreateSettings(
            INavigationService navigationService)
    {
        return (
            "Einstellungen",
            "Darstellung, Cache, Performance, Standardpfade und Datenschutz zentral verwalten.",
            new[]
            {
                Planned(
                    "Cache & Performance",
                    "Cache-Größen, Löschoptionen und spätere Performance-Grenzen verwalten.",
                    navigationService),

                Planned(
                    "Standardpfade",
                    "Bevorzugte Quell-, Ziel- und Arbeitsverzeichnisse festlegen.",
                    navigationService),

                Planned(
                    "Darstellung & Datenschutz",
                    "Oberfläche, lokale Verarbeitung und optionale Komfortfunktionen steuern.",
                    navigationService)
            });
    }

    private static SectionActionViewModel Planned(
        string title,
        string description,
        INavigationService navigationService)
    {
        return new SectionActionViewModel(
            title,
            description,
            "Geplant",
            isAvailable: false,
            navigationId: null,
            navigationService);
    }
}
