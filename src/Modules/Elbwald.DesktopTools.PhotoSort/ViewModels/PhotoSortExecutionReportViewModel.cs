using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

namespace Elbwald.DesktopTools.PhotoSort.ViewModels;

public sealed class PhotoSortExecutionReportViewModel
{
    private PhotoSortExecutionReportViewModel(
        string stateLabel,
        string operationLabel,
        string outcomeText,
        string successfulOperationsText,
        string skippedOperationsText,
        string groupsText,
        string durationText,
        string safetyText,
        string recoveryText,
        string diagnosticsText,
        bool hasDiagnosticsHint,
        bool isSuccess,
        bool isWarning,
        bool isFailure,
        bool requiresRecovery)
    {
        StateLabel = stateLabel;
        OperationLabel = operationLabel;
        OutcomeText = outcomeText;
        SuccessfulOperationsText = successfulOperationsText;
        SkippedOperationsText = skippedOperationsText;
        GroupsText = groupsText;
        DurationText = durationText;
        SafetyText = safetyText;
        RecoveryText = recoveryText;
        DiagnosticsText = diagnosticsText;
        HasDiagnosticsHint = hasDiagnosticsHint;
        IsSuccess = isSuccess;
        IsWarning = isWarning;
        IsFailure = isFailure;
        RequiresRecovery = requiresRecovery;
    }

    public string StateLabel { get; }

    public string OperationLabel { get; }

    public string OutcomeText { get; }

    public string SuccessfulOperationsText { get; }

    public string SkippedOperationsText { get; }

    public string GroupsText { get; }

    public string DurationText { get; }

    public string SafetyText { get; }

    public string RecoveryText { get; }

    public string DiagnosticsText { get; }

    public bool HasDiagnosticsHint { get; }

    public bool IsSuccess { get; }

    public bool IsWarning { get; }

    public bool IsFailure { get; }

    public bool RequiresRecovery { get; }

    public static PhotoSortExecutionReportViewModel FromResult(
        MediaSortLiveExecutionResult result,
        bool isMove,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(result);

        var operationLabel =
            isMove
                ? "Verschieben"
                : "Kopieren";

        var stateLabel =
            result.State switch
            {
                MediaSortLiveExecutionState.Completed =>
                    "Abgeschlossen",

                MediaSortLiveExecutionState.CompletedWithIssues =>
                    "Mit Problemen abgeschlossen",

                MediaSortLiveExecutionState.Cancelled =>
                    "Abgebrochen",

                MediaSortLiveExecutionState.RecoveryRequired =>
                    "Recovery erforderlich",

                MediaSortLiveExecutionState.BlockedByConfirmation =>
                    "Freigabe ungültig",

                MediaSortLiveExecutionState.BlockedByPlan =>
                    "Durch Plan gesperrt",

                MediaSortLiveExecutionState.BlockedByValidation =>
                    "Safety-Prüfung fehlgeschlagen",

                MediaSortLiveExecutionState.BlockedByProcessLock =>
                    "Dateioperation gesperrt",

                MediaSortLiveExecutionState.MoveNotYetEnabled =>
                    "Ausführung gesperrt",

                _ =>
                    "Fehlgeschlagen"
            };

        var outcomeText =
            BuildOutcomeText(
                result,
                isMove);

        var safetyText =
            BuildSafetyText(
                result,
                isMove);

        var recoveryText =
            BuildRecoveryText(
                result,
                isMove);

        var hasDiagnosticsHint =
            result.Problems.Count > 0;

        var diagnosticsText =
            hasDiagnosticsHint
                ? "Diagnose: Werkzeuge → Protokoll. Storage-Ereignisse enthalten Quelle, Datenträgerdaten und Betriebssystemfehler, soweit verfügbar."
                : "Keine Storage-Probleme wurden für diesen Lauf gemeldet.";

        var isSuccess =
            result.State == MediaSortLiveExecutionState.Completed;

        var isWarning =
            result.State is MediaSortLiveExecutionState.CompletedWithIssues
                or MediaSortLiveExecutionState.Cancelled;

        var requiresRecovery =
            result.State == MediaSortLiveExecutionState.RecoveryRequired;

        var isFailure =
            !isSuccess
            && !isWarning;

        return new PhotoSortExecutionReportViewModel(
            stateLabel,
            operationLabel,
            outcomeText,
            $"{result.CompletedOperationCount:N0} / {result.TotalOperationCount:N0}",
            $"{result.SkippedOperationCount:N0}",
            $"{result.CompletedGroupCount:N0} erfolgreich · {result.SkippedGroupCount:N0} übersprungen · {result.TotalGroupCount:N0} geplant",
            FormatDuration(duration),
            safetyText,
            recoveryText,
            diagnosticsText,
            hasDiagnosticsHint,
            isSuccess,
            isWarning,
            isFailure,
            requiresRecovery);
    }

    public static PhotoSortExecutionReportViewModel FromUnexpectedFailure(
        bool isMove,
        TimeSpan duration,
        string message,
        bool cancelled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var operationLabel =
            isMove
                ? "Verschieben"
                : "Kopieren";

        return new PhotoSortExecutionReportViewModel(
            cancelled
                ? "Abgebrochen"
                : "Unerwartet fehlgeschlagen",
            operationLabel,
            message,
            "Unbekannt",
            "Unbekannt",
            "Nicht zuverlässig bestimmbar",
            FormatDuration(duration),
            isMove
                ? "Der Abschlusszustand konnte nicht vollständig als normaler Executor-Result gemeldet werden. Keine weitere Dateioperation starten, bevor Safety und Recovery erneut geprüft wurden."
                : "Der Abschlusszustand konnte nicht vollständig als normaler Executor-Result gemeldet werden. Vor einem neuen Versuch ist eine neue Sicherheitsprüfung erforderlich.",
            "Recovery-Status ist nach diesem Ausnahmefall nicht aus dem Ergebnis ableitbar. Im Zweifel Recovery prüfen; nichts manuell löschen.",
            "Bei einem unerwarteten Fehler zusätzlich Werkzeuge → Protokoll prüfen.",
            hasDiagnosticsHint: true,
            isSuccess: false,
            isWarning: cancelled,
            isFailure: !cancelled,
            requiresRecovery: !cancelled && isMove);
    }

    private static string BuildOutcomeText(
        MediaSortLiveExecutionResult result,
        bool isMove)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        return result.State switch
        {
            MediaSortLiveExecutionState.Completed =>
                isMove
                    ? "Alle gemeldeten Operationen wurden sicher abgeschlossen."
                    : "Alle gemeldeten Kopieroperationen wurden sicher abgeschlossen.",

            MediaSortLiveExecutionState.CompletedWithIssues =>
                "Der Lauf wurde fortgesetzt, aber mindestens eine Datei bzw. Companion-Gruppe wurde sicher übersprungen.",

            MediaSortLiveExecutionState.Cancelled =>
                "Der Lauf wurde abgebrochen. Bereits vollständig abgeschlossene Gruppen bzw. Kopien können vorhanden sein.",

            MediaSortLiveExecutionState.RecoveryRequired =>
                "Der Lauf endete in einem Zustand, der vor weiteren Dateioperationen eine Recovery-Prüfung verlangt.",

            _ =>
                "Die Live-Ausführung wurde nicht vollständig abgeschlossen."
        };
    }

    private static string BuildSafetyText(
        MediaSortLiveExecutionResult result,
        bool isMove)
    {
        return result.State switch
        {
            MediaSortLiveExecutionState.Completed =>
                isMove
                    ? "Quellen wurden nur nach verifizierter persistenter Recovery-Sicherung und verifiziertem endgültigem Ziel entfernt."
                    : "Quellen wurden nicht gelöscht; Zielkopien wurden vor dem Commit verifiziert.",

            MediaSortLiveExecutionState.CompletedWithIssues =>
                isMove
                    ? "Übersprungene Companion-Gruppen wurden nicht teilweise gelöscht. Erfolgreiche Gruppen wurden nach den Move-Safety-Regeln abgeschlossen."
                    : "Übersprungene Gruppen wurden nicht verändert; erfolgreiche Kopien bleiben erhalten.",

            MediaSortLiveExecutionState.RecoveryRequired =>
                "Fail closed: Keine weitere Dateioperation starten, bis der Recovery-Zustand geprüft wurde.",

            MediaSortLiveExecutionState.Cancelled =>
                isMove
                    ? "Ein Abbruch wird nicht mitten in einer bereits destruktiven Gruppenausführung erzwungen. Vor einem neuen Lauf Safety erneut prüfen."
                    : "Bereits committedte Kopien können vorhanden sein; Quellen wurden durch Copy nicht gelöscht.",

            _ =>
                "Die Ausführung blieb gesperrt oder unvollständig. Vor einem neuen Versuch einen neuen Safe Execution Plan erzeugen."
        };
    }

    private static string BuildRecoveryText(
        MediaSortLiveExecutionResult result,
        bool isMove)
    {
        if (result.State == MediaSortLiveExecutionState.RecoveryRequired)
        {
            return "Recovery: erforderlich. Vor jeder weiteren Dateioperation zuerst den Recovery-Bereich prüfen. Verbliebene Recovery-Dateien nicht manuell löschen.";
        }

        if (!isMove)
        {
            return "Recovery: für den erfolgreichen Copy-Pfad ist keine Quelllöschung erforderlich.";
        }

        return result.State switch
        {
            MediaSortLiveExecutionState.Completed =>
                "Recovery: nicht erforderlich. Sicher freigebbare Recovery-Sicherungen werden nach erfolgreichem Gruppenabschluss best effort bereinigt; Reste dürfen aus Sicherheitsgründen bestehen bleiben.",

            MediaSortLiveExecutionState.CompletedWithIssues =>
                "Recovery: für übersprungene Gruppen wurde keine Quelllöschung durchgeführt. Sicher freigebbare Recovery-Sicherungen erfolgreicher Gruppen werden best effort bereinigt.",

            MediaSortLiveExecutionState.Cancelled =>
                "Recovery: vor einem erneuten Move-Lauf prüfen, insbesondere wenn zuvor bereits Gruppen vollständig abgeschlossen wurden.",

            _ =>
                "Recovery: Zustand vor einem erneuten Move-Lauf prüfen."
        };
    }

    private static string FormatDuration(
        TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var totalHours =
            (int)duration.TotalHours;

        return $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
