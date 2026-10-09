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

        if (string.Equals(sectionId, "organize", StringComparison.OrdinalIgnoreCase))
        {
            PrimaryGroupTitle = "Dateien organisieren";
            PrimaryGroupDescription = "Medien sicher übernehmen, physisch strukturieren und einheitlich benennen.";
            SecondaryGroupTitle = "Sammlungen & Vorhaben";
            SecondaryGroupDescription = "Medien logisch zusammenstellen, ohne ihre physische Ablage zu verändern.";

            PrimaryActions = Actions
                .Where(action => !string.Equals(action.Title, "Projekte", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            SecondaryActions = Actions
                .Where(action => string.Equals(action.Title, "Projekte", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        else if (string.Equals(sectionId, "tools", StringComparison.OrdinalIgnoreCase))
        {
            PrimaryGroupTitle = "Dateiwerkzeuge";
            PrimaryGroupDescription = "Spezialfunktionen, die direkt an Medien und ihren Inhalten arbeiten.";
            SecondaryGroupTitle = "Bibliothek & Wartung";
            SecondaryGroupDescription = "Den logischen Medienbestand prüfen und technische Bibliotheksdienste verwalten.";
            TertiaryGroupTitle = "Diagnose";
            TertiaryGroupDescription = "Technische Ereignisse nachvollziehen, wenn etwas nicht wie erwartet läuft.";

            PrimaryActions = Actions
                .Where(action => action.Title is "Metadaten" or "Konvertieren")
                .ToArray();

            SecondaryActions = Actions
                .Where(action => action.Title is "Bibliotheksstatus" or "Medienindex")
                .ToArray();

            TertiaryActions = Actions
                .Where(action => string.Equals(action.Title, "Protokoll", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        else
        {
            PrimaryActions = Actions;
            SecondaryActions = Array.Empty<SectionActionViewModel>();
            TertiaryActions = Array.Empty<SectionActionViewModel>();
        }
    }

    public string Title { get; }

    public string Subtitle { get; }

    public IReadOnlyList<SectionActionViewModel> Actions { get; }

    public IReadOnlyList<SectionActionViewModel> PrimaryActions { get; }

    public IReadOnlyList<SectionActionViewModel> SecondaryActions { get; }

    public IReadOnlyList<SectionActionViewModel> TertiaryActions { get; } = Array.Empty<SectionActionViewModel>();

    public string PrimaryGroupTitle { get; } = string.Empty;

    public string PrimaryGroupDescription { get; } = string.Empty;

    public string SecondaryGroupTitle { get; } = string.Empty;

    public string SecondaryGroupDescription { get; } = string.Empty;

    public string TertiaryGroupTitle { get; } = string.Empty;

    public string TertiaryGroupDescription { get; } = string.Empty;

    public bool HasPrimaryGroupTitle => !string.IsNullOrWhiteSpace(PrimaryGroupTitle);

    public bool HasSecondaryActions => SecondaryActions.Count > 0;

    public bool HasTertiaryActions => TertiaryActions.Count > 0;

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
            "Medien sicher übernehmen, physisch ordnen und logisch zu Sammlungen zusammenstellen.",
            new[]
            {
                new SectionActionViewModel(
                    "Importieren",
                    "Kamera, Smartphone, SD-Karte oder Ordner sicher in die Import-Wartehalle übernehmen.",
                    "Bereit",
                    isAvailable: true,
                    navigationId: "import",
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
                    navigationService),

                new SectionActionViewModel(
                    "Projekte",
                    "Medien logisch zu Sammlungen und Vorhaben zusammenstellen – unabhängig vom aktuellen Dateipfad.",
                    "Bereit",
                    isAvailable: true,
                    navigationId: "projects",
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
                    navigationService),

                new SectionActionViewModel(
                    "Bibliotheksstatus",
                    "Read-only Überblick über konfigurierte Speicherorte, Verfügbarkeit und erste Auffälligkeiten.",
                    "Bereit",
                    isAvailable: true,
                    navigationId: "library-health",
                    navigationService),

                new SectionActionViewModel(
                    "Medienindex",
                    "Lokalen logischen Medienindex aktualisieren und Indexzustand prüfen.",
                    "Bereit",
                    isAvailable: true,
                    navigationId: "media-index",
                    navigationService),

                new SectionActionViewModel(
                    "Protokoll",
                    "Strukturierte Warnungen und Fehler aus Datenträger-, Dateioperations- und Recovery-Pfaden durchsuchen.",
                    "Bereit",
                    isAvailable: true,
                    navigationId: "diagnostics",
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
