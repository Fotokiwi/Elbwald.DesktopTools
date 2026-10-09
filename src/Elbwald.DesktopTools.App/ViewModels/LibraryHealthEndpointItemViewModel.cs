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
        WarningCount = warningCount;
        ProblemCount = problemCount;
        IsAvailable = result.StorageStatus == StorageLocationStatus.Available;

        (StatusTitle, StatusMessage) = GetStatusPresentation(result.StorageStatus);

        FilesAnalyzedText = IsAvailable
            ? $"{FilesAnalyzed:N0} Datei(en) analysiert"
            : "Keine Analyse ausgeführt";

        IssueSummaryText = ProblemCount > 0 || WarningCount > 0
            ? $"{ProblemCount:N0} Problem(e) · {WarningCount:N0} Warnung(en)"
            : "Keine Auffälligkeiten gefunden";

        MissingCaptureDateText = MissingCaptureDateCount > 0
            ? $"{MissingCaptureDateCount:N0} Datei(en) ohne Aufnahmedatum"
            : "Aufnahmedaten vollständig oder nicht relevant";
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public string StatusTitle { get; }

    public string StatusMessage { get; }

    public string ResolvedPath { get; }

    public bool HasResolvedPath { get; }

    public bool IsAvailable { get; }

    public int FilesAnalyzed { get; }

    public int IssueCount { get; }

    public int MissingCaptureDateCount { get; }

    public int WarningCount { get; }

    public int ProblemCount { get; }

    public string FilesAnalyzedText { get; }

    public string IssueSummaryText { get; }

    public string MissingCaptureDateText { get; }

    private static (string Title, string Message) GetStatusPresentation(
        StorageLocationStatus status)
    {
        return status switch
        {
            StorageLocationStatus.Available => (
                "Verfügbar",
                "Der konfigurierte Speicherort wurde über seine Volume-ID eindeutig gefunden."),

            StorageLocationStatus.VolumeUnavailable => (
                "Laufwerk nicht verbunden",
                "Die konfigurierte Volume-ID ist aktuell nicht verfügbar."),

            StorageLocationStatus.PathMissing => (
                "Unterordner fehlt",
                "Das Laufwerk wurde erkannt, der konfigurierte relative Pfad existiert aber nicht."),

            StorageLocationStatus.AmbiguousVolume => (
                "Mehrdeutiges Laufwerk",
                "Die Volume-ID konnte nicht eindeutig aufgelöst werden. Der Endpunkt bleibt gesperrt."),

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
