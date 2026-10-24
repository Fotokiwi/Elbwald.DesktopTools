using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class LibraryHealthEndpointItemViewModel
{
    public LibraryHealthEndpointItemViewModel(
        LibraryHealthEndpointResult result,
        int warningCount,
        int problemCount)
    {
        ArgumentNullException.ThrowIfNull(result);

        Id = result.Endpoint.Id;
        Name = result.Endpoint.Name;
        Kind = GetKindLabel(result.Endpoint.Kind);
        ResolvedPath = result.ResolvedPath ?? string.Empty;
        HasResolvedPath = !string.IsNullOrWhiteSpace(result.ResolvedPath);
        FilesAnalyzed = result.FilesAnalyzed;
        IssueCount = result.IssueCount;
        MissingCaptureDateCount = result.MissingCaptureDateCount;
        ImageCount = result.ImageCount;
        MissingDimensionsCount = result.MissingDimensionsCount;
        ConflictingCaptureDateCount = result.ConflictingCaptureDateCount;
        SuspiciousCaptureDateCount = result.SuspiciousCaptureDateCount;
        MetadataErrorCount = result.MetadataErrorCount;
        WarningCount = warningCount;
        ProblemCount = problemCount;
        IsAvailable = result.StorageStatus == StorageLocationStatus.Available;
        IsOffline = result.StorageStatus == StorageLocationStatus.VolumeUnavailable;
        IndexedLocationCount = result.IndexedLocationCount;
        ReachableIndexedLocationCount = result.ReachableIndexedLocationCount;
        MissingIndexedLocationCount = result.MissingIndexedLocationCount;
        HistoricalIndexedLocationCount = result.HistoricalIndexedLocationCount;

        (StatusTitle, StatusMessage) = GetStatusPresentation(result.StorageStatus);

        FilesAnalyzedText = IsAvailable
            ? $"{FilesAnalyzed:N0} Datei(en) analysiert"
            : IsOffline
                ? "Dateisystemscan pausiert · Speicherort offline"
                : "Keine Analyse ausgeführt";

        IssueSummaryText = ProblemCount > 0 || WarningCount > 0
            ? $"{ProblemCount:N0} Problem(e) · {WarningCount:N0} Warnung(en)"
            : IsOffline
                ? "Offline ist kein Datenverlust"
                : "Keine Auffälligkeiten gefunden";

        MissingCaptureDateText = MissingCaptureDateCount > 0
            ? $"{MissingCaptureDateCount:N0} Datei(en) ohne Aufnahmedatum"
            : "Aufnahmedaten vollständig oder nicht relevant";

        MetadataQualityText = ImageCount == 0
            ? "Keine Bildmedien geprüft"
            : $"Metadaten: {ConflictingCaptureDateCount:N0} Datumskonflikt(e) · {SuspiciousCaptureDateCount:N0} unplausibel · {MissingDimensionsCount:N0} ohne Abmessungen";

        HasIndexData = IndexedLocationCount > 0;
        IndexStatusText = BuildIndexStatusText();
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public string StatusTitle { get; }

    public string StatusMessage { get; }

    public string ResolvedPath { get; }

    public bool HasResolvedPath { get; }

    public bool IsAvailable { get; }

    public bool IsOffline { get; }

    public int FilesAnalyzed { get; }

    public int IssueCount { get; }

    public int MissingCaptureDateCount { get; }

    public int ImageCount { get; }

    public int MissingDimensionsCount { get; }

    public int ConflictingCaptureDateCount { get; }

    public int SuspiciousCaptureDateCount { get; }

    public int MetadataErrorCount { get; }

    public int WarningCount { get; }

    public int ProblemCount { get; }

    public int IndexedLocationCount { get; }

    public int ReachableIndexedLocationCount { get; }

    public int MissingIndexedLocationCount { get; }

    public int HistoricalIndexedLocationCount { get; }

    public bool HasIndexData { get; }

    public string FilesAnalyzedText { get; }

    public string IssueSummaryText { get; }

    public string MissingCaptureDateText { get; }

    public string MetadataQualityText { get; }

    public string IndexStatusText { get; }

    private string BuildIndexStatusText()
    {
        if (!HasIndexData)
        {
            return "Noch keine Indexorte für diesen Speicherort";
        }

        if (IsOffline)
        {
            return $"Index kennt {IndexedLocationCount:N0} Medienort(e) · derzeit nicht physisch prüfbar";
        }

        if (MissingIndexedLocationCount > 0)
        {
            return $"Index: {ReachableIndexedLocationCount:N0} erreichbar · {MissingIndexedLocationCount:N0} fehlen am erwarteten Pfad";
        }

        if (HistoricalIndexedLocationCount > 0)
        {
            return $"Index: {ReachableIndexedLocationCount:N0} erreichbar · {HistoricalIndexedLocationCount:N0} historische Ort(e)";
        }

        return $"Index: {ReachableIndexedLocationCount:N0} erreichbare Medienorte";
    }

    private static (string Title, string Message) GetStatusPresentation(
        StorageLocationStatus status)
    {
        return status switch
        {
            StorageLocationStatus.Available => (
                "Verfügbar",
                "Der konfigurierte Speicherort wurde über seine Volume-ID eindeutig gefunden."),

            StorageLocationStatus.VolumeUnavailable => (
                "Offline / nicht verbunden",
                "Das konfigurierte Laufwerk ist momentan nicht verfügbar. Elbwald wertet dessen Medien deshalb nicht als verloren."),

            StorageLocationStatus.PathMissing => (
                "Unterordner fehlt",
                "Das Laufwerk wurde erkannt, aber der konfigurierte Bibliotheksordner existiert nicht mehr. Das ist ein echtes Problem und sollte geprüft werden."),

            StorageLocationStatus.AmbiguousVolume => (
                "Mehrdeutiges Laufwerk",
                "Die Volume-ID konnte nicht eindeutig aufgelöst werden. Der Endpunkt bleibt aus Sicherheitsgründen gesperrt."),

            _ => (
                "Ungültige Konfiguration",
                "Die gespeicherte Endpunktkonfiguration ist unvollständig oder widersprüchlich.")
        };
    }

    private static string GetKindLabel(
        StorageEndpointKind kind)
    {
        return kind switch
        {
            StorageEndpointKind.Import => "Import",
            StorageEndpointKind.MediaLibrary => "Bibliothek",
            StorageEndpointKind.Backup => "Backup",
            StorageEndpointKind.Export => "Export",
            StorageEndpointKind.WorkingDirectory => "Arbeitsverzeichnis",
            StorageEndpointKind.Archive => "Archiv",
            _ => "Benutzerdefiniert"
        };
    }
}
