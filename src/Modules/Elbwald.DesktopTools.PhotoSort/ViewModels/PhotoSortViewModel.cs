using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;
using Elbwald.DesktopTools.UI.Formatting;

namespace Elbwald.DesktopTools.PhotoSort.ViewModels;

public sealed class PhotoSortViewModel
    : ObservableObject
{
    private const int PreviewItemLimit = 500;
    private const int IssueItemLimit = 100;
    private const int DateContextHintLimit = 30;

    private readonly IMediaAnalyzer _mediaAnalyzer;
    private readonly IMediaSourcePicker _sourcePicker;
    private readonly IMediaSortPlanner _sortPlanner;
    private readonly IMediaSortExecutionPlanner _executionPlanner;
    private readonly IMediaSortExecutionExecutor _executionExecutor;

    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _durationTimer;

    private CancellationTokenSource? _cancellation;
    private MediaSortPlan? _currentSortPlan;
    private MediaSortExecutionPlan? _currentExecutionPlan;
    private PhotoSortExecutionReportViewModel? _executionReport;

    private string _sourcePath =
        string.Empty;

    private string _destinationPath =
        string.Empty;

    private bool _includeSubdirectories = true;
    private bool _isBusy;
    private bool _hasPlan;
    private bool _hasExecutionPlan;
    private bool _approveDateReviews;
    private bool _approveSourceDeletion;
    private bool _isProgressIndeterminate;
    private double _progressPercent;

    private string _selectedRule =
        "Jahr / Monat";

    private string _selectedOperation =
        "Kopieren";

    private string _selectedDateConfidence =
        "Hoch (empfohlen)";

    private string _statusMessage =
        "Quelle und Ziel auswählen. 0037 erlaubt verifiziertes Kopieren und recovery-gesichertes Verschieben.";

    private string _elapsedTimeText =
        "Dauer: 00:00:00";

    private string _plannedFiles =
        "0";

    private string _plannedSize =
        "0 B";

    private string _missingDateCount =
        "0";

    private string _conflictCount =
        "0";

    private string _ignoredCount =
        "0";

    private string _dateContextHintCount =
        "0";

    private string _planState =
        "Noch keine Vorschau";

    private string _previewSummary =
        "Noch kein Dry Run vorhanden.";

    private string _executionSafetyMessage =
        "Nach dem Dry Run kann eine erneute Sicherheitsprüfung für Kopieren oder sicheres Verschieben erzeugt werden.";

    private string _confirmationText =
        string.Empty;

    private string _executionFingerprint =
        string.Empty;

    private IReadOnlyList<PhotoSortPlanItemViewModel> _previewItems =
        Array.Empty<PhotoSortPlanItemViewModel>();

    private IReadOnlyList<PhotoSortIssueViewModel> _issues =
        Array.Empty<PhotoSortIssueViewModel>();

    private IReadOnlyList<PhotoSortDateContextHintViewModel> _dateContextHints =
        Array.Empty<PhotoSortDateContextHintViewModel>();

    public PhotoSortViewModel(
        IMediaAnalyzer mediaAnalyzer,
        IMediaSourcePicker sourcePicker,
        IMediaSortPlanner sortPlanner,
        IMediaSortExecutionPlanner executionPlanner,
        IMediaSortExecutionExecutor executionExecutor)
    {
        ArgumentNullException.ThrowIfNull(mediaAnalyzer);
        ArgumentNullException.ThrowIfNull(sourcePicker);
        ArgumentNullException.ThrowIfNull(sortPlanner);
        ArgumentNullException.ThrowIfNull(executionPlanner);
        ArgumentNullException.ThrowIfNull(executionExecutor);

        _mediaAnalyzer = mediaAnalyzer;
        _sourcePicker = sourcePicker;
        _sortPlanner = sortPlanner;
        _executionPlanner = executionPlanner;
        _executionExecutor = executionExecutor;

        _durationTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(1)
            };

        _durationTimer.Tick +=
            (_, _) => UpdateDuration();

        ChooseSourceCommand =
            new AsyncRelayCommand(
                ChooseSourceAsync,
                CanChoosePath);

        ChooseDestinationCommand =
            new AsyncRelayCommand(
                ChooseDestinationAsync,
                CanChoosePath);

        BuildPreviewCommand =
            new AsyncRelayCommand(
                BuildPreviewAsync,
                CanBuildPreview);

        PrepareExecutionCommand =
            new AsyncRelayCommand(
                PrepareExecutionAsync,
                CanPrepareExecution);

        ExecuteOperationCommand =
            new AsyncRelayCommand(
                ExecuteOperationAsync,
                CanExecuteOperation);

        CancelCommand =
            new RelayCommand(
                Cancel,
                () => IsBusy);
    }

    public IAsyncRelayCommand ChooseSourceCommand { get; }

    public IAsyncRelayCommand ChooseDestinationCommand { get; }

    public IAsyncRelayCommand BuildPreviewCommand { get; }

    public IAsyncRelayCommand PrepareExecutionCommand { get; }

    public IAsyncRelayCommand ExecuteOperationCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IReadOnlyList<string> RuleOptions { get; } =
        new[]
        {
            "Jahr / Monat",
            "Jahr / Kamera / Monat"
        };

    public IReadOnlyList<string> OperationOptions { get; } =
        new[]
        {
            "Kopieren",
            "Verschieben"
        };

    public IReadOnlyList<string> DateConfidenceOptions { get; } =
        new[]
        {
            "Sehr hoch",
            "Hoch (empfohlen)",
            "Mittel"
        };

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (SetProperty(
                    ref _sourcePath,
                    value))
            {
                InvalidateDryRun();
                BuildPreviewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(
                    ref _destinationPath,
                    value))
            {
                InvalidateDryRun();
                BuildPreviewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IncludeSubdirectories
    {
        get => _includeSubdirectories;
        set
        {
            if (SetProperty(
                    ref _includeSubdirectories,
                    value))
            {
                InvalidateDryRun();
            }
        }
    }

    public string SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(
                    ref _selectedRule,
                    value))
            {
                InvalidateDryRun();
            }
        }
    }

    public string SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            if (SetProperty(
                    ref _selectedOperation,
                    value))
            {
                ApproveSourceDeletion = false;

                OnPropertyChanged(
                    nameof(IsMoveSelected));

                OnPropertyChanged(
                    nameof(ShowSourceDeletionApproval));

                OnPropertyChanged(
                    nameof(RequiredConfirmationText));

                OnPropertyChanged(
                    nameof(ConfirmationPlaceholder));

                OnPropertyChanged(
                    nameof(ExecuteActionText));

                OnPropertyChanged(
                    nameof(ExecutionInstructionText));

                InvalidateDryRun();
            }
        }
    }

    public string SelectedDateConfidence
    {
        get => _selectedDateConfidence;
        set
        {
            if (SetProperty(
                    ref _selectedDateConfidence,
                    value))
            {
                InvalidateDryRun();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(
                    ref _isBusy,
                    value))
            {
                return;
            }

            OnPropertyChanged(
                nameof(IsInputEnabled));

            ChooseSourceCommand.NotifyCanExecuteChanged();
            ChooseDestinationCommand.NotifyCanExecuteChanged();
            BuildPreviewCommand.NotifyCanExecuteChanged();
            PrepareExecutionCommand.NotifyCanExecuteChanged();
            ExecuteOperationCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsInputEnabled =>
        !IsBusy;

    public bool HasPlan
    {
        get => _hasPlan;
        private set
        {
            if (SetProperty(
                    ref _hasPlan,
                    value))
            {
                PrepareExecutionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasExecutionPlan
    {
        get => _hasExecutionPlan;
        private set
        {
            if (SetProperty(
                    ref _hasExecutionPlan,
                    value))
            {
                OnPropertyChanged(
                    nameof(ShowDateReviewApproval));

                OnPropertyChanged(
                    nameof(ShowSourceDeletionApproval));

                ExecuteOperationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool ApproveDateReviews
    {
        get => _approveDateReviews;
        set
        {
            if (SetProperty(
                    ref _approveDateReviews,
                    value))
            {
                ExecuteOperationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool ShowDateReviewApproval =>
        HasExecutionPlan
        && _currentExecutionPlan?.HasDateReviewApprovalRequirement == true;

    public bool ApproveSourceDeletion
    {
        get => _approveSourceDeletion;
        set
        {
            if (SetProperty(
                    ref _approveSourceDeletion,
                    value))
            {
                ExecuteOperationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsMoveSelected =>
        string.Equals(
            SelectedOperation,
            "Verschieben",
            StringComparison.Ordinal);

    public bool ShowSourceDeletionApproval =>
        HasExecutionPlan
        && IsMoveSelected;

    public string RequiredConfirmationText =>
        IsMoveSelected
            ? MediaSortExecutionConfirmation.MoveRequiredText
            : MediaSortExecutionConfirmation.CopyRequiredText;

    public string ConfirmationPlaceholder =>
        RequiredConfirmationText
        + " eingeben";

    public string ExecuteActionText =>
        IsMoveSelected
            ? "Jetzt sicher verschieben"
            : "Jetzt kopieren";

    public string ExecutionInstructionText =>
        IsMoveSelected
            ? "Beim Verschieben werden zuerst persistente Recovery-Sicherungen und vollständige SHA-256-verifizierte Zielkopien aller Gruppenmitglieder erzeugt. Erst danach werden Quellen entfernt. Nicht zuverlässig lesbare Quellen überspringen die gesamte betroffene Gruppe; bei möglichem Datenträger-I/O-Fehler wird deutlich gewarnt."
            : "Beim Kopieren werden reale Dateien im Ziel angelegt. Vor jedem Commit werden Quelle und temporäre Kopie erneut per SHA-256 geprüft. Nicht zuverlässig lesbare Quellen überspringen die betroffene Gruppe; bei möglichem Datenträger-I/O-Fehler wird deutlich gewarnt.";

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(
            ref _isProgressIndeterminate,
            value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(
            ref _progressPercent,
            value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(
            ref _statusMessage,
            value);
    }

    public string ElapsedTimeText
    {
        get => _elapsedTimeText;
        private set => SetProperty(
            ref _elapsedTimeText,
            value);
    }

    public string PlannedFiles
    {
        get => _plannedFiles;
        private set => SetProperty(
            ref _plannedFiles,
            value);
    }

    public string PlannedSize
    {
        get => _plannedSize;
        private set => SetProperty(
            ref _plannedSize,
            value);
    }

    public string MissingDateCount
    {
        get => _missingDateCount;
        private set => SetProperty(
            ref _missingDateCount,
            value);
    }

    public string ConflictCount
    {
        get => _conflictCount;
        private set => SetProperty(
            ref _conflictCount,
            value);
    }

    public string IgnoredCount
    {
        get => _ignoredCount;
        private set => SetProperty(
            ref _ignoredCount,
            value);
    }

    public string DateContextHintCount
    {
        get => _dateContextHintCount;
        private set => SetProperty(
            ref _dateContextHintCount,
            value);
    }

    public string PlanState
    {
        get => _planState;
        private set => SetProperty(
            ref _planState,
            value);
    }

    public string PreviewSummary
    {
        get => _previewSummary;
        private set => SetProperty(
            ref _previewSummary,
            value);
    }

    public string ExecutionSafetyMessage
    {
        get => _executionSafetyMessage;
        private set => SetProperty(
            ref _executionSafetyMessage,
            value);
    }

    public string ConfirmationText
    {
        get => _confirmationText;
        set
        {
            if (SetProperty(
                    ref _confirmationText,
                    value))
            {
                ExecuteOperationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ExecutionFingerprint
    {
        get => _executionFingerprint;
        private set => SetProperty(
            ref _executionFingerprint,
            value);
    }

    public PhotoSortExecutionReportViewModel? ExecutionReport
    {
        get => _executionReport;
        private set
        {
            if (SetProperty(
                    ref _executionReport,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasExecutionReport));
            }
        }
    }

    public bool HasExecutionReport =>
        ExecutionReport is not null;

    public IReadOnlyList<PhotoSortPlanItemViewModel> PreviewItems
    {
        get => _previewItems;
        private set => SetProperty(
            ref _previewItems,
            value);
    }

    public IReadOnlyList<PhotoSortIssueViewModel> Issues
    {
        get => _issues;
        private set
        {
            if (SetProperty(
                    ref _issues,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasIssues));
            }
        }
    }

    public bool HasIssues =>
        Issues.Count > 0;

    public IReadOnlyList<PhotoSortDateContextHintViewModel> DateContextHints
    {
        get => _dateContextHints;
        private set
        {
            if (SetProperty(
                    ref _dateContextHints,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasDateContextHints));
            }
        }
    }

    public bool HasDateContextHints =>
        DateContextHints.Count > 0;

    private bool CanChoosePath()
    {
        return !IsBusy;
    }

    private bool CanBuildPreview()
    {
        return !IsBusy
               && !string.IsNullOrWhiteSpace(SourcePath)
               && !string.IsNullOrWhiteSpace(DestinationPath);
    }

    private bool CanPrepareExecution()
    {
        return !IsBusy
               && HasPlan
               && _currentSortPlan is not null;
    }

    private bool CanExecuteOperation()
    {
        if (IsBusy
            || _currentExecutionPlan is null
            || !HasExecutionPlan)
        {
            return false;
        }

        if (_currentExecutionPlan.HasBlockingPlanningIssues
            || _currentExecutionPlan.ValidationIssues.Count > 0)
        {
            return false;
        }

        if (_currentExecutionPlan.HasDateReviewApprovalRequirement
            && !ApproveDateReviews)
        {
            return false;
        }

        if (IsMoveSelected
            && !ApproveSourceDeletion)
        {
            return false;
        }

        return string.Equals(
            ConfirmationText.Trim(),
            RequiredConfirmationText,
            StringComparison.Ordinal);
    }

    private async Task ChooseSourceAsync()
    {
        var path =
            await _sourcePicker.PickFolderAsync();

        if (!string.IsNullOrWhiteSpace(path))
        {
            SourcePath =
                path;

            StatusMessage =
                "Quelle gewählt. Zielordner auswählen und Dry Run erzeugen.";
        }
    }

    private async Task ChooseDestinationAsync()
    {
        var path =
            await _sourcePicker.PickFolderAsync();

        if (!string.IsNullOrWhiteSpace(path))
        {
            DestinationPath =
                path;

            StatusMessage =
                "Ziel gewählt. Der Dry Run verändert weiterhin keine Dateien.";
        }
    }

    private async Task BuildPreviewAsync()
    {
        if (!CanBuildPreview())
        {
            return;
        }

        _cancellation?.Dispose();
        _cancellation =
            new CancellationTokenSource();

        IsBusy = true;
        ExecutionReport = null;
        HasPlan = false;
        InvalidateExecutionPlan();
        _currentSortPlan = null;
        IsProgressIndeterminate = true;
        ProgressPercent = 0;

        PreviewItems =
            Array.Empty<PhotoSortPlanItemViewModel>();

        Issues =
            Array.Empty<PhotoSortIssueViewModel>();

        DateContextHints =
            Array.Empty<PhotoSortDateContextHintViewModel>();

        PlannedFiles = "0";
        PlannedSize = "0 B";
        MissingDateCount = "0";
        ConflictCount = "0";
        IgnoredCount = "0";
        DateContextHintCount = "0";
        PlanState = "Analyse läuft";
        PreviewSummary =
            "Dry Run wird vorbereitet …";

        StartDuration();

        try
        {
            var progress =
                new Progress<MediaAnalysisProgress>(
                    UpdateAnalysisProgress);

            var analysis =
                await _mediaAnalyzer.AnalyzeAsync(
                    SourcePath,
                    new MediaAnalysisOptions
                    {
                        Recursive =
                            IncludeSubdirectories,
                        IncludeUnknownFiles =
                            true
                    },
                    progress,
                    _cancellation.Token);

            _cancellation.Token.ThrowIfCancellationRequested();

            IsProgressIndeterminate = true;
            StatusMessage =
                "Sortierplan wird read-only berechnet …";
            PlanState =
                "Planung läuft";

            var sortOptions =
                new MediaSortOptions
                {
                    Rule =
                        SelectedRule == "Jahr / Kamera / Monat"
                            ? MediaSortRule.YearCameraMonth
                            : MediaSortRule.YearMonth,

                    OperationKind =
                        SelectedOperation == "Verschieben"
                            ? FileOperationKind.Move
                            : FileOperationKind.Copy,

                    MinimumDateConfidence =
                        SelectedDateConfidence switch
                        {
                            "Sehr hoch" =>
                                MediaDateConfidence.VeryHigh,

                            "Mittel" =>
                                MediaDateConfidence.Medium,

                            _ =>
                                MediaDateConfidence.High
                        }
                };

            var plan =
                await Task.Run(
                    () =>
                        _sortPlanner.CreatePlan(
                            SourcePath,
                            DestinationPath,
                            analysis.Files,
                            sortOptions,
                            _cancellation.Token),
                    _cancellation.Token);

            _currentSortPlan =
                plan;

            ApplyPlan(
                plan);

            StatusMessage =
                BuildCompletionStatus(
                    plan);
        }
        catch (OperationCanceledException)
        {
            StatusMessage =
                "Dry Run abgebrochen. Es wurden keine Dateien verändert.";

            PlanState =
                "Abgebrochen";
        }
        catch (Exception exception)
        {
            StatusMessage =
                "Dry Run konnte nicht abgeschlossen werden: "
                + exception.GetBaseException().Message;

            PlanState =
                "Fehler";
        }
        finally
        {
            StopDuration();

            IsBusy = false;
            IsProgressIndeterminate = false;

            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private async Task PrepareExecutionAsync()
    {
        if (!CanPrepareExecution()
            || _currentSortPlan is null)
        {
            return;
        }

        _cancellation?.Dispose();
        _cancellation =
            new CancellationTokenSource();

        IsBusy = true;
        InvalidateExecutionPlan();
        IsProgressIndeterminate = true;
        ProgressPercent = 0;
        StatusMessage =
            "Safe Execution Plan wird erneut gegen Dateisystem, Speicher und Recovery geprüft …";
        PlanState =
            "Sicherheitsprüfung";
        StartDuration();

        try
        {
            var executionPlan =
                await _executionPlanner.CreateAsync(
                    _currentSortPlan,
                    _cancellation.Token);

            _currentExecutionPlan =
                executionPlan;

            HasExecutionPlan = true;
            ConfirmationText =
                string.Empty;

            ApproveDateReviews = false;
            ApproveSourceDeletion = false;

            ExecutionFingerprint =
                executionPlan.Fingerprint.Length > 16
                    ? executionPlan.Fingerprint[..16]
                      + "…"
                    : executionPlan.Fingerprint;

            if (executionPlan.HasBlockingPlanningIssues
                || executionPlan.ValidationIssues.Count > 0)
            {
                ExecutionSafetyMessage =
                    BuildBlockingExecutionMessage(
                        executionPlan);

                StatusMessage =
                    "Sicherheitsprüfung abgeschlossen: Ausführung bleibt wegen eines nicht bestätigbaren Safety-/Recovery-Blockers gesperrt.";

                PlanState =
                    "Ausführung gesperrt";
            }
            else
            {
                var reviewText =
                    executionPlan.HasDateReviewApprovalRequirement
                        ? " Datums-Prüffälle müssen zusätzlich ausdrücklich bestätigt werden."
                        : string.Empty;

                var operationText =
                    IsMoveSelected
                        ? "sicheres Verschieben"
                        : "Kopieren";

                var moveText =
                    IsMoveSelected
                        ? $" Persistente Recovery-Spitze: {FormatBytes(executionPlan.PeakPersistentRecoveryBytes)}. "
                          + "Vor der ersten Quelllöschung müssen Recovery, Quelle und endgültiges Ziel für die gesamte Gruppe verifiziert sein."
                        : string.Empty;

                ExecutionSafetyMessage =
                    $"Bereit für explizite Freigabe: {executionPlan.OperationCount:N0} Operation(en) für {operationText}, "
                    + $"{executionPlan.GroupCount:N0} Gruppe(n), "
                    + $"{executionPlan.SidecarOperationCount:N0} Sidecar(s)."
                    + moveText
                    + reviewText;

                StatusMessage =
                    IsMoveSelected
                        ? "Sicherheitsprüfung abgeschlossen. Für Verschieben sind Löschfreigabe und die Texteingabe VERSCHIEBEN erforderlich."
                        : "Sicherheitsprüfung abgeschlossen. Zum Kopieren ist jetzt eine explizite Freigabe erforderlich.";

                PlanState =
                    "Freigabe erforderlich";
            }

            ProgressPercent = 100;
            IsProgressIndeterminate = false;
        }
        catch (OperationCanceledException)
        {
            StatusMessage =
                "Sicherheitsprüfung abgebrochen. Es wurden keine Dateien verändert.";

            PlanState =
                "Abgebrochen";
        }
        catch (Exception exception)
        {
            StatusMessage =
                "Sicherheitsprüfung fehlgeschlagen: "
                + exception.GetBaseException().Message;

            PlanState =
                "Fehler";
        }
        finally
        {
            StopDuration();
            IsBusy = false;
            IsProgressIndeterminate = false;

            _cancellation?.Dispose();
            _cancellation = null;

            ExecuteOperationCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ExecuteOperationAsync()
    {
        if (!CanExecuteOperation()
            || _currentExecutionPlan is null)
        {
            return;
        }

        var executionPlan =
            _currentExecutionPlan;

        var confirmation =
            new MediaSortExecutionConfirmation(
                executionPlan.Fingerprint,
                ConfirmationText,
                ApproveDateReviews,
                ApproveSourceDeletion);

        _cancellation?.Dispose();
        _cancellation =
            new CancellationTokenSource();

        IsBusy = true;
        ExecutionReport = null;
        IsProgressIndeterminate = false;
        ProgressPercent = 0;
        PlanState =
            IsMoveSelected
                ? "Verschieben läuft"
                : "Kopieren läuft";

        StatusMessage =
            IsMoveSelected
                ? "Sicheres Verschieben läuft: Recovery sichern → Zielkopie schreiben/flushen → SHA-256 verifizieren → erst danach Quelle entfernen."
                : "Sichere Kopie läuft: Quellen werden nicht gelöscht. Jede Kopie wird vor dem Commit per SHA-256 verifiziert.";
        StartDuration();

        try
        {
            var hasExecutionProblems =
                false;

            var progress =
                new Progress<MediaSortLiveExecutionProgress>(
                    value =>
                    {
                        ProgressPercent =
                            value.Percentage;

                        if (value.LatestProblem is not null)
                        {
                            hasExecutionProblems =
                                true;

                            PlanState =
                                value.LatestProblem.SuspectsPhysicalDevice
                                    ? "Datenträgerwarnung"
                                    : "Quelldatei übersprungen";

                            ExecutionSafetyMessage =
                                BuildExecutionProblemMessage(
                                    value.LatestProblem);

                            StatusMessage =
                                $"{value.SkippedOperationCount:N0} Operation(en) in "
                                + $"{value.SkippedGroupCount:N0} Gruppe(n) sicher übersprungen. "
                                + "Der Vorgang läuft mit den übrigen Dateien weiter; Abbrechen ist jederzeit möglich.";

                            return;
                        }

                        if (hasExecutionProblems)
                        {
                            PlanState =
                                "Läuft mit Warnung";
                        }

                        StatusMessage =
                            IsMoveSelected
                                ? $"Sicheres Verschieben … {value.CompletedOperationCount:N0} erfolgreich, "
                                  + $"{value.SkippedOperationCount:N0} übersprungen, "
                                  + $"{value.TotalOperationCount:N0} geplant."
                                : $"Sicheres Kopieren … {value.CompletedOperationCount:N0} erfolgreich, "
                                  + $"{value.SkippedOperationCount:N0} übersprungen, "
                                  + $"{value.TotalOperationCount:N0} geplant.";
                    });

            var result =
                await _executionExecutor.ExecuteAsync(
                    executionPlan,
                    confirmation,
                    progress,
                    _cancellation.Token);

            ExecutionReport =
                PhotoSortExecutionReportViewModel.FromResult(
                    result,
                    IsMoveSelected,
                    _stopwatch.Elapsed);

            switch (result.State)
            {
                case MediaSortLiveExecutionState.Completed:
                    ProgressPercent = 100;
                    PlanState =
                        IsMoveSelected
                            ? "Verschieben abgeschlossen"
                            : "Kopieren abgeschlossen";

                    StatusMessage =
                        IsMoveSelected
                            ? $"Sicheres Verschieben abgeschlossen: "
                              + $"{result.CompletedOperationCount:N0} von "
                              + $"{result.TotalOperationCount:N0} Operation(en). "
                              + "Jede Quelle wurde erst nach verifizierter persistenter Recovery-Sicherung und verifiziertem endgültigem Ziel entfernt."
                            : $"Sicheres Kopieren abgeschlossen: "
                              + $"{result.CompletedOperationCount:N0} von "
                              + $"{result.TotalOperationCount:N0} Operation(en). "
                              + "Die Quelldateien wurden nicht verändert.";

                    ExecutionSafetyMessage =
                        "Ausführung abgeschlossen. Für weitere Dateioperationen bitte einen neuen Dry Run erzeugen.";

                    _currentSortPlan = null;
                    _currentExecutionPlan = null;
                    HasExecutionPlan = false;
                    ConfirmationText = string.Empty;
                    ApproveDateReviews = false;
                    ApproveSourceDeletion = false;
                    break;

                case MediaSortLiveExecutionState.CompletedWithIssues:
                {
                    ProgressPercent = 100;
                    PlanState =
                        "Mit Problemen abgeschlossen";

                    var firstProblem =
                        result.Problems.FirstOrDefault();

                    StatusMessage =
                        $"Ausführung abgeschlossen: {result.CompletedOperationCount:N0} Operation(en) erfolgreich, "
                        + $"{result.SkippedOperationCount:N0} Operation(en) in "
                        + $"{result.SkippedGroupCount:N0} Gruppe(n) sicher übersprungen. "
                        + "Übersprungene Quellen wurden nicht gelöscht.";

                    ExecutionSafetyMessage =
                        firstProblem is null
                            ? "Ausführung mit übersprungenen Quelldateien abgeschlossen. Bitte Probleme prüfen und anschließend einen neuen Dry Run erzeugen."
                            : BuildExecutionProblemMessage(
                                firstProblem)
                              + " Für weitere Dateioperationen bitte einen neuen Dry Run erzeugen.";

                    Issues =
                        result.Problems
                            .Take(
                                IssueItemLimit)
                            .Select(problem =>
                                new PhotoSortIssueViewModel(
                                    problem))
                            .ToArray();

                    _currentSortPlan = null;
                    _currentExecutionPlan = null;
                    HasExecutionPlan = false;
                    ConfirmationText = string.Empty;
                    ApproveDateReviews = false;
                    ApproveSourceDeletion = false;
                    break;
                }

                case MediaSortLiveExecutionState.Cancelled:
                    PlanState =
                        "Abgebrochen";

                    StatusMessage =
                        IsMoveSelected
                            ? "Verschieben wurde vor einer destruktiven Phase der laufenden Gruppe abgebrochen. Bereits vollständig abgeschlossene frühere Gruppen können verschoben sein; vor einem neuen Versuch ist eine neue Sicherheitsprüfung erforderlich."
                            : "Kopieren wurde sicher abgebrochen. Bereits vollständig committedte Kopien können vorhanden sein; Quelldateien wurden nicht gelöscht.";

                    RequireNewExecutionSafetyCheck();
                    break;

                case MediaSortLiveExecutionState.RecoveryRequired:
                    PlanState =
                        "Recovery erforderlich";

                    StatusMessage =
                        IsMoveSelected
                            ? "Die Move-Gruppenausführung wurde nach einem Ziel-Commit oder einer Quelllöschung unterbrochen. Persistente Recovery-Sicherungen bleiben erhalten. Vor jeder weiteren Dateioperation zuerst Recovery prüfen."
                            : "Die Gruppen-Ausführung wurde nach einem Commit unterbrochen. Keine Quelldatei wurde gelöscht. Bitte zuerst den Recovery-Status prüfen.";

                    RequireNewExecutionSafetyCheck(
                        "Recovery erforderlich. Vor einem neuen Versuch zuerst Recovery prüfen und danach die Sicherheitsprüfung neu erzeugen.");
                    break;

                case MediaSortLiveExecutionState.MoveNotYetEnabled:
                    PlanState =
                        "Ausführung gesperrt";

                    StatusMessage =
                        result.ErrorMessage
                        ?? "Der Executor hat einen veralteten Move-Sperrstatus geliefert. Bitte Sicherheitsprüfung und Build prüfen.";

                    RequireNewExecutionSafetyCheck();
                    break;

                default:
                    PlanState =
                        "Ausführung gesperrt";

                    StatusMessage =
                        result.ErrorMessage
                        ?? "Die sichere Ausführung wurde nicht gestartet oder nicht abgeschlossen.";

                    RequireNewExecutionSafetyCheck();
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            PlanState =
                "Abgebrochen";

            ExecutionReport =
                PhotoSortExecutionReportViewModel.FromUnexpectedFailure(
                    IsMoveSelected,
                    _stopwatch.Elapsed,
                    "Die Live-Ausführung wurde abgebrochen, bevor ein normaler Abschlussstatus geliefert werden konnte.",
                    cancelled: true);

            StatusMessage =
                IsMoveSelected
                    ? "Verschieben wurde abgebrochen. Vor einem neuen Versuch bitte Recovery-Status und Sicherheitsprüfung erneut prüfen."
                    : "Kopieren wurde abgebrochen. Bitte den Recovery-Status vor einem neuen Versuch prüfen.";

            RequireNewExecutionSafetyCheck();
        }
        catch (Exception exception)
        {
            PlanState =
                "Fehler";

            ExecutionReport =
                PhotoSortExecutionReportViewModel.FromUnexpectedFailure(
                    IsMoveSelected,
                    _stopwatch.Elapsed,
                    "Sichere Ausführung ist unerwartet fehlgeschlagen: "
                    + exception.GetBaseException().Message,
                    cancelled: false);

            StatusMessage =
                "Sichere Ausführung ist unerwartet fehlgeschlagen: "
                + exception.GetBaseException().Message
                + " Bitte vor weiteren Dateioperationen den Recovery-Status prüfen.";

            RequireNewExecutionSafetyCheck(
                "Ausführung fehlgeschlagen. Vor einem neuen Versuch Safety/Recovery erneut prüfen.");
        }
        finally
        {
            StopDuration();
            IsBusy = false;
            IsProgressIndeterminate = false;

            _cancellation?.Dispose();
            _cancellation = null;

            PrepareExecutionCommand.NotifyCanExecuteChanged();
            ExecuteOperationCommand.NotifyCanExecuteChanged();
        }
    }

    private void RequireNewExecutionSafetyCheck(
        string? message = null)
    {
        _currentExecutionPlan = null;
        HasExecutionPlan = false;
        ConfirmationText = string.Empty;
        ApproveDateReviews = false;
        ApproveSourceDeletion = false;
        ExecutionFingerprint = string.Empty;

        ExecutionSafetyMessage =
            message
            ?? "Vor einem weiteren Live-Versuch muss die Sicherheitsprüfung erneut erzeugt werden.";
    }

    private void InvalidateDryRun()
    {
        _currentSortPlan = null;
        HasPlan = false;
        InvalidateExecutionPlan();

        PreviewItems =
            Array.Empty<PhotoSortPlanItemViewModel>();

        Issues =
            Array.Empty<PhotoSortIssueViewModel>();

        DateContextHints =
            Array.Empty<PhotoSortDateContextHintViewModel>();

        PlannedFiles = "0";
        PlannedSize = "0 B";
        MissingDateCount = "0";
        ConflictCount = "0";
        IgnoredCount = "0";
        DateContextHintCount = "0";
        PreviewSummary =
            "Einstellungen geändert – neuer Dry Run erforderlich.";

        if (!IsBusy)
        {
            PlanState =
                "Dry Run erforderlich";
        }
    }

    private void InvalidateExecutionPlan()
    {
        _currentExecutionPlan = null;
        HasExecutionPlan = false;
        ConfirmationText = string.Empty;
        ApproveDateReviews = false;
        ApproveSourceDeletion = false;
        ExecutionFingerprint = string.Empty;

        ExecutionSafetyMessage =
            "Nach dem Dry Run kann eine erneute Sicherheitsprüfung für Kopieren oder recovery-gesichertes Verschieben erzeugt werden.";

        ExecuteOperationCommand?.NotifyCanExecuteChanged();
    }

    private static string BuildExecutionProblemMessage(
        MediaSortExecutionProblem problem)
    {
        var health =
            problem.StorageHealth;

        var deviceParts =
            new[]
            {
                health.Model,
                health.PhysicalDevicePath,
                health.VolumeDevicePath
            }
            .Where(value =>
                !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var deviceText =
            deviceParts.Length > 0
                ? " Datenträger: "
                  + string.Join(
                      " · ",
                      deviceParts)
                  + "."
                : string.Empty;

        var mountText =
            !string.IsNullOrWhiteSpace(health.MountPoint)
                ? $" Einbindung: {health.MountPoint}."
                : string.Empty;

        var hardwareText =
            string.Empty;

        if (!string.IsNullOrWhiteSpace(health.SerialNumber))
        {
            hardwareText +=
                $" Seriennummer: {health.SerialNumber}.";
        }

        if (health.IsRotational is not null)
        {
            hardwareText +=
                health.IsRotational.Value
                    ? " Gerätetyp: rotierend (HDD)."
                    : " Gerätetyp: nicht rotierend.";
        }

        if (health.CapacityBytes is > 0)
        {
            hardwareText +=
                $" Kapazität: {FormatBytes(health.CapacityBytes.Value)}.";
        }

        var osText =
            !string.IsNullOrWhiteSpace(health.OperatingSystemMessage)
                ? $" Betriebssystem: {health.OperatingSystemMessage}"
                : string.Empty;

        var smartText =
            !string.IsNullOrWhiteSpace(health.SmartNote)
                ? " "
                  + health.SmartNote
                : string.Empty;

        return problem.Message
               + $" Datei: {problem.SourcePath}."
               + deviceText
               + mountText
               + hardwareText
               + " "
               + health.Summary
               + osText
               + smartText;
    }

    private static string BuildBlockingExecutionMessage(
        MediaSortExecutionPlan executionPlan)
    {
        var blockers =
            executionPlan.PlanningIssues
                .Where(issue =>
                    issue.Kind
                    != MediaSortExecutionIssueKind.ReviewRequiresConfirmation)
                .Concat(
                    executionPlan.ValidationIssues)
                .Take(3)
                .ToArray();

        if (blockers.Length == 0)
        {
            return "Nicht freigegeben: Der Plan enthält einen blockierenden Sicherheitszustand, "
                   + "der noch nicht näher beschrieben werden konnte.";
        }

        var messages =
            blockers
                .Select(issue =>
                    issue.Message)
                .Where(message =>
                    !string.IsNullOrWhiteSpace(message))
                .ToArray();

        var summary =
            messages.Length == 0
                ? "Der Plan enthält mindestens einen blockierenden Safety-/Recovery-Zustand."
                : string.Join(
                    " | ",
                    messages);

        var additionalCount =
            executionPlan.PlanningIssues.Count(issue =>
                issue.Kind
                != MediaSortExecutionIssueKind.ReviewRequiresConfirmation)
            + executionPlan.ValidationIssues.Count
            - blockers.Length;

        if (additionalCount > 0)
        {
            summary +=
                $" | +{additionalCount:N0} weiterer Blocker.";
        }

        if (executionPlan.HasDateReviewApprovalRequirement)
        {
            summary +=
                " Datums-Prüffälle sind separat bestätigbar und sind nicht der hier angezeigte Blocker.";
        }

        return "Nicht freigegeben: "
               + summary;
    }

    private void ApplyPlan(
        MediaSortPlan plan)
    {
        var conflictIndexes =
            plan.OperationPlan.Conflicts
                .Select(conflict =>
                    conflict.OperationIndex)
                .ToHashSet();

        PreviewItems =
            plan.Items
                .Take(
                    PreviewItemLimit)
                .Select(item =>
                    new PhotoSortPlanItemViewModel(
                        item,
                        conflictIndexes.Contains(
                            item.OperationIndex)))
                .ToArray();

        DateContextHints =
            plan.DateContextHints
                .Take(
                    DateContextHintLimit)
                .Select(hint =>
                    new PhotoSortDateContextHintViewModel(
                        hint))
                .ToArray();

        Issues =
            plan.Issues
                .OrderByDescending(issue =>
                    issue.Severity)
                .ThenBy(
                    issue => issue.Path,
                    StringComparer.CurrentCultureIgnoreCase)
                .Take(
                    IssueItemLimit)
                .Select(issue =>
                    new PhotoSortIssueViewModel(
                        issue))
                .ToArray();

        PlannedFiles =
            plan.PlannedFileCount.ToString("N0");

        PlannedSize =
            FormatBytes(
                plan.PlannedBytes);

        MissingDateCount =
            plan.DateReviewCount.ToString("N0");

        ConflictCount =
            plan.ConflictCount.ToString("N0");

        IgnoredCount =
            plan.IgnoredNonImageCount.ToString("N0");

        DateContextHintCount =
            plan.DateContextHintCount.ToString("N0");

        PlanState =
            !plan.CanExecute
                ? "Plan prüfen"
                : plan.DateReviewCount > 0
                  || plan.DateContextHintCount > 0
                    ? "Plan mit Datumshinweisen"
                    : "Konfliktfreier Plan";

        PreviewSummary =
            plan.PlannedFileCount > PreviewItemLimit
                ? $"{PreviewItemLimit:N0} von {plan.PlannedFileCount:N0} geplanten Dateien angezeigt."
                : $"{plan.PlannedFileCount:N0} geplante Datei(en) angezeigt.";

        HasPlan = true;
        ProgressPercent = 100;

        PrepareExecutionCommand.NotifyCanExecuteChanged();
    }

    private static string BuildCompletionStatus(
        MediaSortPlan plan)
    {
        if (plan.ProblemCount > 0)
        {
            return $"Dry Run abgeschlossen: {plan.ProblemCount:N0} Problem(e). Es wird nichts ausgeführt.";
        }

        var details =
            new List<string>();

        if (plan.DateReviewCount > 0)
        {
            details.Add(
                $"{plan.DateReviewCount:N0} Datumsfall/-fälle zur Prüfung");
        }

        if (plan.DateContextHintCount > 0)
        {
            details.Add(
                $"{plan.DateContextHintCount:N0} Serien-/Ordnerhinweis(e)");
        }

        if (details.Count == 0)
        {
            return $"Dry Run abgeschlossen: {plan.PlannedFileCount:N0} Datei(en) konfliktfrei geplant. "
                   + "Es wird nichts ausgeführt.";
        }

        return $"Dry Run abgeschlossen: {plan.PlannedFileCount:N0} Datei(en) geplant, "
               + string.Join(
                   ", ",
                   details)
               + ". Es wird nichts ausgeführt.";
    }

    private void UpdateAnalysisProgress(
        MediaAnalysisProgress progress)
    {
        switch (progress.Stage)
        {
            case MediaAnalysisStage.Scanning:
                IsProgressIndeterminate = true;
                ProgressPercent = 0;

                StatusMessage =
                    $"Quelle wird gescannt … {progress.FilesDiscovered:N0} Datei(en) gefunden.";
                break;

            case MediaAnalysisStage.ReadingMetadata:
                IsProgressIndeterminate = false;

                ProgressPercent =
                    progress.MetadataTotal <= 0
                        ? 100
                        : progress.MetadataProcessed
                          * 100d
                          / progress.MetadataTotal;

                StatusMessage =
                    $"Lese Aufnahmedaten … {progress.MetadataProcessed:N0} von {progress.MetadataTotal:N0} · "
                    + $"{progress.MetadataCacheHits:N0} Cache-Treffer.";
                break;

            case MediaAnalysisStage.Completed:
                IsProgressIndeterminate = false;
                ProgressPercent = 100;
                break;
        }
    }

    private void Cancel()
    {
        _cancellation?.Cancel();
    }

    private void StartDuration()
    {
        _stopwatch.Restart();

        UpdateDuration();

        _durationTimer.Start();
    }

    private void StopDuration()
    {
        _stopwatch.Stop();

        _durationTimer.Stop();

        UpdateDuration();
    }

    private void UpdateDuration()
    {
        ElapsedTimeText =
            "Dauer: "
            + ProcessingDurationFormatter.Format(
                _stopwatch.Elapsed);
    }

    private static string FormatBytes(
        long bytes)
    {
        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        };

        var value =
            Math.Max(
                0,
                (double)bytes);

        var unitIndex = 0;

        while (value >= 1024
               && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:N0} {units[unitIndex]}"
            : $"{value:N1} {units[unitIndex]}";
    }
}
