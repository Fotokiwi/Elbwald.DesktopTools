using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.OperationHistory;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.UI.Formatting;

namespace Elbwald.DesktopTools.PhotoSort.ViewModels;

public sealed class PhotoSortViewModel
    : ObservableObject
{
    private const int PreviewItemLimit = 500;
    private const int IssueItemLimit = 100;
    private const int DateContextHintLimit = 30;

    private readonly IMediaAnalyzer _mediaAnalyzer;
    private readonly IMediaDateResolver _mediaDateResolver;
    private readonly IMediaSourcePicker _sourcePicker;
    private readonly IMediaSortPlanner _sortPlanner;
    private readonly IMediaSortExecutionPlanner _executionPlanner;
    private readonly IMediaSortExecutionExecutor _executionExecutor;
    private readonly IMediaIndexService _mediaIndex;
    private readonly IOperationHistoryStore _operationHistory;
    private readonly IMediaImportHistoryStore _importHistoryStore;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageLocationResolver;

    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _durationTimer;

    private CancellationTokenSource? _cancellation;
    private MediaSortPlan? _currentSortPlan;
    private MediaSortExecutionPlan? _currentExecutionPlan;
    private PhotoSortExecutionReportViewModel? _executionReport;

    private IReadOnlyList<MediaAnalyzedFile> _currentAnalyzedFiles =
        Array.Empty<MediaAnalyzedFile>();

    private IReadOnlyDictionary<string, MediaSortMetadataContext> _currentMetadataBySourcePath =
        new Dictionary<string, MediaSortMetadataContext>(StringComparer.Ordinal);

    private readonly Dictionary<string, MediaSortDateDecision> _dateDecisions =
        new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    private string _sourceContextText =
        "Import-Wartehalle";

    private string _destinationContextText =
        "Medienbibliothek";

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

    private string _pathPattern =
        "{year}/{month}";

    private string _selectedPreviewFilter =
        "Alle";

    private string _selectedOperation =
        "Kopieren";

    private string _selectedDateConfidence =
        "Empfohlen";

    private string _statusMessage =
        "Quelle und Ziel werden vorbereitet. Wähle anschließend eine Ordnerstruktur und erstelle eine Vorschau.";

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
        "Noch keine Vorschau erstellt.";

    private string _executionSafetyMessage =
        "Nach der Vorschau prüft Elbwald Digital Quelle, Ziel und mögliche Konflikte noch einmal, bevor Dateien verändert werden.";

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

    private IReadOnlyList<PhotoSortDateReviewItemViewModel> _dateReviewItems =
        Array.Empty<PhotoSortDateReviewItemViewModel>();

    public PhotoSortViewModel(
        IMediaAnalyzer mediaAnalyzer,
        IMediaDateResolver mediaDateResolver,
        IMediaSourcePicker sourcePicker,
        IMediaSortPlanner sortPlanner,
        IMediaSortExecutionPlanner executionPlanner,
        IMediaSortExecutionExecutor executionExecutor,
        IMediaIndexService mediaIndex,
        IOperationHistoryStore operationHistory,
        IMediaImportHistoryStore importHistoryStore,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver)
    {
        ArgumentNullException.ThrowIfNull(mediaAnalyzer);
        ArgumentNullException.ThrowIfNull(mediaDateResolver);
        ArgumentNullException.ThrowIfNull(sourcePicker);
        ArgumentNullException.ThrowIfNull(sortPlanner);
        ArgumentNullException.ThrowIfNull(executionPlanner);
        ArgumentNullException.ThrowIfNull(executionExecutor);
        ArgumentNullException.ThrowIfNull(mediaIndex);
        ArgumentNullException.ThrowIfNull(operationHistory);
        ArgumentNullException.ThrowIfNull(importHistoryStore);
        ArgumentNullException.ThrowIfNull(storageSettingsStore);
        ArgumentNullException.ThrowIfNull(storageLocationResolver);

        _mediaAnalyzer = mediaAnalyzer;
        _mediaDateResolver = mediaDateResolver;
        _sourcePicker = sourcePicker;
        _sortPlanner = sortPlanner;
        _executionPlanner = executionPlanner;
        _executionExecutor = executionExecutor;
        _mediaIndex = mediaIndex;
        _operationHistory = operationHistory;
        _importHistoryStore = importHistoryStore;
        _storageSettingsStore = storageSettingsStore;
        _storageLocationResolver = storageLocationResolver;

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

        UseAllSuggestedDatesCommand =
            new AsyncRelayCommand(
                UseAllSuggestedDatesAsync,
                () => DateReviewItems.Any(item => item.HasSuggestedDate) && !IsBusy);

        SortAllUnclearDatesCommand =
            new AsyncRelayCommand(
                SortAllUnclearDatesAsync,
                () => DateReviewItems.Count > 0 && !IsBusy);

        CancelCommand =
            new RelayCommand(
                Cancel,
                () => IsBusy);

        UseYearMonthPresetCommand =
            new RelayCommand(
                () => PathPattern = "{year}/{month}");

        UseYearEventPresetCommand =
            new RelayCommand(
                () => PathPattern = "{year}/{title}");

        UseCameraPresetCommand =
            new RelayCommand(
                () => PathPattern = "{year}/{camera}/{month}");

        UseYearMonthDayPresetCommand =
            new RelayCommand(
                () => PathPattern = "{year}/{month}/{day}");

        UseYearFolderPresetCommand =
            new RelayCommand(
                () => PathPattern = "{year}/{folder}");

        UseMediaTypePresetCommand =
            new RelayCommand(
                () => PathPattern = "{year}/{type}/{month}");

        InsertTokenCommand =
            new RelayCommand<string>(
                token =>
                {
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        PathPattern += token;
                    }
                });

        _ = LoadSuggestedPathsAsync();
    }

    public IAsyncRelayCommand ChooseSourceCommand { get; }

    public IAsyncRelayCommand ChooseDestinationCommand { get; }

    public IAsyncRelayCommand BuildPreviewCommand { get; }

    public IAsyncRelayCommand PrepareExecutionCommand { get; }

    public IAsyncRelayCommand ExecuteOperationCommand { get; }

    public IAsyncRelayCommand UseAllSuggestedDatesCommand { get; }

    public IAsyncRelayCommand SortAllUnclearDatesCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand UseYearMonthPresetCommand { get; }

    public IRelayCommand UseYearEventPresetCommand { get; }

    public IRelayCommand UseCameraPresetCommand { get; }

    public IRelayCommand UseYearMonthDayPresetCommand { get; }

    public IRelayCommand UseYearFolderPresetCommand { get; }

    public IRelayCommand UseMediaTypePresetCommand { get; }

    public IRelayCommand<string> InsertTokenCommand { get; }

    public IReadOnlyList<string> RuleOptions { get; } =
        new[]
        {
            "Jahr / Monat",
            "Jahr / Monat / Tag",
            "Jahr / Quellordner",
            "Jahr / Titel",
            "Jahr / Kamera / Monat",
            "Jahr / Medientyp / Monat"
        };

    public IReadOnlyList<string> PreviewFilterOptions { get; } =
        new[]
        {
            "Alle",
            "Bereit",
            "Datum prüfen",
            "Konflikte"
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
            "Sehr vorsichtig",
            "Empfohlen",
            "Mehr Datumsangaben nutzen"
        };

    public string SourceContextText
    {
        get => _sourceContextText;
        private set => SetProperty(ref _sourceContextText, value);
    }

    public string DestinationContextText
    {
        get => _destinationContextText;
        private set => SetProperty(ref _destinationContextText, value);
    }

    public string OperationHelpText =>
        IsMoveSelected
            ? "Die Originale werden erst entfernt, nachdem die neuen Kopien vollständig geprüft wurden."
            : "Die Originale bleiben am bisherigen Ort. Für den Einstieg ist Kopieren die sichere Empfehlung.";

    public string PreviewActionText =>
        HasPlan
            ? "Vorschau aktualisieren"
            : "Vorschau erstellen";

    public string StructurePreviewText =>
        PathPattern switch
        {
            "{year}/{month}" => "2026 / 10",
            "{year}/{month}/{day}" => "2026 / 10 / 11",
            "{year}/{folder}" => "2026 / Urlaub",
            "{year}/{title}" => "2026 / Familienfeier",
            "{year}/{camera}/{month}" => "2026 / Kamera / 10",
            "{year}/{type}/{month}" => "2026 / Bilder / 10",
            _ => PathPattern.Replace("/", " / ", StringComparison.Ordinal)
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
                _dateDecisions.Clear();
                _currentAnalyzedFiles = Array.Empty<MediaAnalyzedFile>();
                _currentMetadataBySourcePath =
                    new Dictionary<string, MediaSortMetadataContext>(StringComparer.Ordinal);
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

    public string PathPattern
    {
        get => _pathPattern;
        set
        {
            if (SetProperty(
                    ref _pathPattern,
                    value))
            {
                InvalidateDryRun();
                BuildPreviewCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(StructurePreviewText));
            }
        }
    }

    public string SelectedPreviewFilter
    {
        get => _selectedPreviewFilter;
        set
        {
            if (SetProperty(
                    ref _selectedPreviewFilter,
                    value))
            {
                OnPropertyChanged(nameof(FilteredPreviewItems));
            }
        }
    }

    public IReadOnlyList<PhotoSortPlanItemViewModel> FilteredPreviewItems =>
        SelectedPreviewFilter switch
        {
            "Bereit" => PreviewItems.Where(item => item.IsReady).ToArray(),
            "Datum prüfen" => PreviewItems.Where(item => item.NeedsDateReview).ToArray(),
            "Konflikte" => PreviewItems.Where(item => item.HasConflict).ToArray(),
            _ => PreviewItems
        };

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
                    nameof(ShowConfirmationText));

                OnPropertyChanged(
                    nameof(ExecuteActionText));

                OnPropertyChanged(
                    nameof(ExecutionInstructionText));

                OnPropertyChanged(
                    nameof(OperationHelpText));

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
            UseAllSuggestedDatesCommand.NotifyCanExecuteChanged();
            SortAllUnclearDatesCommand.NotifyCanExecuteChanged();
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
                OnPropertyChanged(nameof(PreviewActionText));
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
            : string.Empty;

    public string ConfirmationPlaceholder =>
        IsMoveSelected
            ? RequiredConfirmationText + " eingeben"
            : string.Empty;

    public bool ShowConfirmationText =>
        IsMoveSelected;

    public string ExecuteActionText =>
        IsMoveSelected
            ? $"{PlannedFiles} Dateien sicher verschieben"
            : $"{PlannedFiles} Dateien kopieren";

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
        private set
        {
            if (SetProperty(ref _plannedFiles, value))
            {
                OnPropertyChanged(nameof(ExecuteActionText));
            }
        }
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
        private set
        {
            if (SetProperty(
                    ref _previewItems,
                    value))
            {
                OnPropertyChanged(nameof(FilteredPreviewItems));
            }
        }
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

    public IReadOnlyList<PhotoSortDateReviewItemViewModel> DateReviewItems
    {
        get => _dateReviewItems;
        private set
        {
            if (SetProperty(
                    ref _dateReviewItems,
                    value))
            {
                OnPropertyChanged(nameof(HasDateReviews));
                OnPropertyChanged(nameof(DateReviewCountText));
                UseAllSuggestedDatesCommand.NotifyCanExecuteChanged();
                SortAllUnclearDatesCommand.NotifyCanExecuteChanged();
                PrepareExecutionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasDateReviews =>
        DateReviewItems.Count > 0;

    public string DateReviewCountText =>
        DateReviewItems.Count == 1
            ? "1 Datei braucht deine Entscheidung"
            : $"{DateReviewItems.Count:N0} Dateien brauchen deine Entscheidung";

    private bool CanChoosePath()
    {
        return !IsBusy;
    }

    private bool CanBuildPreview()
    {
        return !IsBusy
               && !string.IsNullOrWhiteSpace(SourcePath)
               && !string.IsNullOrWhiteSpace(DestinationPath)
               && !string.IsNullOrWhiteSpace(PathPattern);
    }

    private bool CanPrepareExecution()
    {
        return !IsBusy
               && HasPlan
               && _currentSortPlan is not null
               && !HasDateReviews;
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

        if (!IsMoveSelected)
        {
            return true;
        }

        return string.Equals(
            ConfirmationText.Trim(),
            RequiredConfirmationText,
            StringComparison.Ordinal);
    }

    private async Task LoadSuggestedPathsAsync()
    {
        try
        {
            var lastImport =
                await _importHistoryStore.LoadLastAsync();

            var settings =
                await _storageSettingsStore.LoadAsync();

            var importEndpoint =
                settings.Find(StorageEndpoint.DefaultImportId);

            var libraryEndpoint =
                settings.Find(StorageEndpoint.DefaultLibraryId);

            string? importRoot = null;

            if (importEndpoint is not null)
            {
                var importResolution =
                    _storageLocationResolver.Resolve(importEndpoint.Location);

                if (importResolution.IsAvailable)
                {
                    importRoot = importResolution.ResolvedPath;
                }
            }

            string? libraryRoot = null;

            if (libraryEndpoint is not null)
            {
                var libraryResolution =
                    _storageLocationResolver.Resolve(libraryEndpoint.Location);

                if (libraryResolution.IsAvailable)
                {
                    libraryRoot = libraryResolution.ResolvedPath;
                }
            }

            if (lastImport is not null
                && !string.IsNullOrWhiteSpace(lastImport.DestinationPath)
                && Directory.Exists(lastImport.DestinationPath))
            {
                SourcePath = lastImport.DestinationPath;
                SourceContextText = "Zuletzt importierter Ordner";
            }
            else if (!string.IsNullOrWhiteSpace(importRoot)
                     && Directory.Exists(importRoot))
            {
                SourcePath = importRoot;
                SourceContextText = "Import-Wartehalle";
            }
            else
            {
                SourceContextText = "Quelle auswählen";
            }

            if (!string.IsNullOrWhiteSpace(libraryRoot)
                && Directory.Exists(libraryRoot))
            {
                DestinationPath = libraryRoot;
                DestinationContextText = "Medienbibliothek";
            }
            else
            {
                DestinationContextText = "Ziel auswählen";
            }

            StatusMessage =
                CanBuildPreview()
                    ? "Quelle und Ziel sind vorbereitet. Wähle eine Ordnerstruktur und erstelle eine Vorschau."
                    : "Quelle oder Ziel fehlen noch. Du kannst jederzeit auch eigene Ordner auswählen.";
        }
        catch (Exception exception)
        {
            StatusMessage =
                "Die vorgeschlagenen Ordner konnten nicht automatisch geladen werden. Du kannst Quelle und Ziel manuell auswählen.";

            Debug.WriteLine(
                $"PhotoSort-Standardpfade konnten nicht geladen werden: {exception.Message}");
        }
    }

    private async Task ChooseSourceAsync()
    {
        var path =
            await _sourcePicker.PickFolderAsync();

        if (!string.IsNullOrWhiteSpace(path))
        {
            SourcePath =
                path;

            SourceContextText =
                "Eigener Ordner";

            StatusMessage =
                "Quelle gewählt. Prüfe das Ziel und erstelle anschließend eine Vorschau.";
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

            DestinationContextText =
                "Eigenes Ziel";

            StatusMessage =
                "Ziel gewählt. Eine Vorschau zeigt dir vorab genau, wie die Ordner angelegt werden.";
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

        DateReviewItems =
            Array.Empty<PhotoSortDateReviewItemViewModel>();

        PlannedFiles = "0";
        PlannedSize = "0 B";
        MissingDateCount = "0";
        ConflictCount = "0";
        IgnoredCount = "0";
        DateContextHintCount = "0";
        PlanState = "Analyse läuft";
        PreviewSummary =
            "Vorschau wird vorbereitet …";

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

            _currentAnalyzedFiles = analysis.Files;

            IsProgressIndeterminate = true;
            StatusMessage =
                "Vorschau der neuen Ordnerstruktur wird berechnet …";
            PlanState =
                "Planung läuft";

            _currentMetadataBySourcePath =
                await BuildTemplateMetadataAsync(
                    analysis.Files,
                    PathPattern,
                    _cancellation.Token);

            var sortOptions =
                BuildCurrentSortOptions();

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
                "Vorschau abgebrochen. Es wurden keine Dateien verändert.";

            PlanState =
                "Abgebrochen";
        }
        catch (Exception exception)
        {
            StatusMessage =
                "Vorschau konnte nicht abgeschlossen werden: "
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
                if (IsMoveSelected)
                {
                    ExecutionSafetyMessage =
                        $"Bereit zum sicheren Verschieben: {executionPlan.OperationCount:N0} Dateioperation(en). "
                        + "Die Originale werden erst entfernt, wenn die Zielkopien vollständig geprüft wurden.";

                    StatusMessage =
                        "Sicherheitsprüfung abgeschlossen. Prüfe die Löschfreigabe und bestätige anschließend mit VERSCHIEBEN.";

                    PlanState =
                        "Bestätigung erforderlich";
                }
                else
                {
                    ExecutionSafetyMessage =
                        $"Bereit zum Kopieren: {executionPlan.OperationCount:N0} Dateioperation(en). "
                        + "Vorhandene Dateien werden nicht überschrieben und die Originale bleiben unverändert.";

                    StatusMessage =
                        "Sicherheitsprüfung abgeschlossen. Du kannst den Kopiervorgang jetzt starten.";

                    PlanState =
                        "Bereit zum Kopieren";
                }
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

            if (result.State == MediaSortLiveExecutionState.Completed
                && result.CompletedOperationCount == result.TotalOperationCount)
            {
                await TryRecordOperationHistoryAsync(
                    executionPlan,
                    CancellationToken.None);
            }

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
                        "Ausführung abgeschlossen. Für weitere Dateioperationen bitte eine neue Vorschau erstellen.";

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
                            ? "Ausführung mit übersprungenen Quelldateien abgeschlossen. Bitte Probleme prüfen und anschließend eine neue Vorschau erstellen."
                            : BuildExecutionProblemMessage(
                                firstProblem)
                              + " Für weitere Dateioperationen bitte eine neue Vorschau erstellen.";

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

        DateReviewItems =
            Array.Empty<PhotoSortDateReviewItemViewModel>();

        PlannedFiles = "0";
        PlannedSize = "0 B";
        MissingDateCount = "0";
        ConflictCount = "0";
        IgnoredCount = "0";
        DateContextHintCount = "0";
        PreviewSummary =
            "Einstellungen geändert – neue Vorschau erforderlich.";

        if (!IsBusy)
        {
            PlanState =
                "Neue Vorschau erforderlich";
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
            "Nach der Vorschau kann die letzte Sicherheitsprüfung für Kopieren oder sicheres Verschieben gestartet werden.";

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
                " Offene Datumsentscheidungen werden separat geklärt und sind nicht der hier angezeigte Blocker.";
        }

        return "Nicht freigegeben: "
               + summary;
    }

    private async Task<IReadOnlyDictionary<string, MediaSortMetadataContext>> BuildTemplateMetadataAsync(
        IReadOnlyList<MediaAnalyzedFile> files,
        string pattern,
        CancellationToken cancellationToken)
    {
        if (!pattern.Contains("{title", StringComparison.OrdinalIgnoreCase)
            && !pattern.Contains("{description", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, MediaSortMetadataContext>(StringComparer.Ordinal);
        }

        var comparer =
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        var result =
            new Dictionary<string, MediaSortMetadataContext>(comparer);

        foreach (var analyzed in files.Where(file => file.File.MediaType == MediaFileType.Image))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hash =
                await ComputeSha256Async(
                    analyzed.File.FullPath,
                    cancellationToken);

            var item =
                await _mediaIndex.FindByHashAsync(
                    hash,
                    analyzed.File.Length,
                    cancellationToken);

            if (item is null)
            {
                continue;
            }

            var metadata =
                await _mediaIndex.GetMetadataAsync(
                    item.Id,
                    cancellationToken);

            result[Path.GetFullPath(analyzed.File.FullPath)] =
                new MediaSortMetadataContext(
                    metadata.TitleOverridden
                        ? metadata.Title
                        : null,
                    metadata.DescriptionOverridden
                        ? metadata.Description
                        : null);
        }

        return result;
    }

    private async Task TryRecordOperationHistoryAsync(
        MediaSortExecutionPlan executionPlan,
        CancellationToken cancellationToken)
    {
        try
        {
            var entries =
                new List<OperationHistoryEntry>();

            var batchId =
                "sort-" + Guid.NewGuid().ToString("N");

            var operationType =
                IsMoveSelected
                    ? "SortMove"
                    : "SortCopy";

            var completedUtc =
                DateTimeOffset.UtcNow;

            foreach (var operation in executionPlan.Groups.SelectMany(group => group.Operations))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(operation.DestinationPath))
                {
                    return;
                }

                var hash =
                    await ComputeSha256Async(
                        operation.DestinationPath,
                        cancellationToken);

                string? mediaItemId = null;

                if (!operation.IsSidecar)
                {
                    var indexed =
                        await _mediaIndex.FindByHashAsync(
                            hash,
                            operation.SourceSnapshot.Length,
                            cancellationToken);

                    mediaItemId =
                        indexed?.Id;
                }

                entries.Add(
                    new OperationHistoryEntry(
                        batchId,
                        operationType,
                        operation.SourcePath,
                        operation.DestinationPath,
                        hash,
                        operation.SourceSnapshot.Length,
                        mediaItemId,
                        completedUtc));
            }

            if (entries.Count > 0)
            {
                await _operationHistory.AppendBatchAsync(
                    entries,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or InvalidOperationException)
        {
            ExecutionSafetyMessage +=
                " Vorgang erfolgreich, aber Benutzer-Historie konnte nicht vollständig protokolliert werden: "
                + exception.GetBaseException().Message;
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous
                | FileOptions.SequentialScan);

        var hash =
            await SHA256.HashDataAsync(
                stream,
                cancellationToken);

        return Convert.ToHexString(hash);
    }

    private MediaSortOptions BuildCurrentSortOptions()
    {
        return new MediaSortOptions
        {
            PathPattern = PathPattern,
            MetadataBySourcePath = _currentMetadataBySourcePath,
            DateDecisionsBySourcePath = new Dictionary<string, MediaSortDateDecision>(
                _dateDecisions,
                _dateDecisions.Comparer),
            OperationKind =
                SelectedOperation == "Verschieben"
                    ? FileOperationKind.Move
                    : FileOperationKind.Copy,
            MinimumDateConfidence =
                SelectedDateConfidence switch
                {
                    "Sehr vorsichtig" => MediaDateConfidence.VeryHigh,
                    "Mehr Datumsangaben nutzen" => MediaDateConfidence.Medium,
                    _ => MediaDateConfidence.High
                }
        };
    }

    private async Task ApplyDateDecisionAsync(
        string path,
        MediaSortDateDecision decision)
    {
        if (IsBusy
            || _currentAnalyzedFiles.Count == 0)
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(path);
        _dateDecisions[normalizedPath] = decision;

        await RebuildPlanFromCurrentAnalysisAsync();
    }

    private async Task UseAllSuggestedDatesAsync()
    {
        if (IsBusy
            || _currentAnalyzedFiles.Count == 0)
        {
            return;
        }

        foreach (var item in DateReviewItems.Where(item => item.Resolution.Value.HasValue))
        {
            _dateDecisions[Path.GetFullPath(item.Path)] =
                MediaSortDateDecision.UseDate(
                    item.Resolution.Value!.Value,
                    "Vorschlag für die Sortierung vom Nutzer gesammelt bestätigt.");
        }

        await RebuildPlanFromCurrentAnalysisAsync();
    }

    private async Task SortAllUnclearDatesAsync()
    {
        if (IsBusy
            || _currentAnalyzedFiles.Count == 0)
        {
            return;
        }

        foreach (var item in DateReviewItems)
        {
            _dateDecisions[Path.GetFullPath(item.Path)] =
                MediaSortDateDecision.SortAsUnclear();
        }

        await RebuildPlanFromCurrentAnalysisAsync();
    }

    private async Task RebuildPlanFromCurrentAnalysisAsync()
    {
        if (_currentAnalyzedFiles.Count == 0)
        {
            return;
        }

        IsBusy = true;
        InvalidateExecutionPlan();
        IsProgressIndeterminate = true;
        StatusMessage = "Deine Datumsentscheidung wird in die Vorschau übernommen …";
        PlanState = "Vorschau wird aktualisiert";

        try
        {
            var sortOptions = BuildCurrentSortOptions();

            var plan = await Task.Run(() =>
                _sortPlanner.CreatePlan(
                    SourcePath,
                    DestinationPath,
                    _currentAnalyzedFiles,
                    sortOptions));

            _currentSortPlan = plan;
            ApplyPlan(plan);
            StatusMessage = BuildCompletionStatus(plan);
        }
        catch (Exception exception)
        {
            StatusMessage =
                "Die Vorschau konnte nach der Datumsentscheidung nicht aktualisiert werden: "
                + exception.GetBaseException().Message;
            PlanState = "Fehler";
        }
        finally
        {
            IsProgressIndeterminate = false;
            IsBusy = false;
        }
    }

    private void OpenDateReviewFile(string path)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
        }
        catch (Exception exception)
        {
            StatusMessage =
                "Datei konnte nicht mit der Standardanwendung geöffnet werden: "
                + exception.GetBaseException().Message;
        }
    }

    private IReadOnlyList<PhotoSortDateReviewItemViewModel> BuildDateReviewItems(
        MediaSortPlan plan)
    {
        var dateIssuePaths = plan.Issues
            .Where(issue => issue.Kind is
                MediaSortIssueKind.MissingCaptureDate
                or MediaSortIssueKind.DateReview
                or MediaSortIssueKind.InsufficientDateConfidence
                or MediaSortIssueKind.DateConflict)
            .Select(issue => Path.GetFullPath(issue.Path))
            .Distinct(_dateDecisions.Comparer)
            .ToHashSet(_dateDecisions.Comparer);

        return _currentAnalyzedFiles
            .Where(file => dateIssuePaths.Contains(Path.GetFullPath(file.File.FullPath)))
            .Select(file =>
            {
                var resolution = _mediaDateResolver.Resolve(file);

                return new PhotoSortDateReviewItemViewModel(
                    file.File.FullPath,
                    resolution,
                    ApplyDateDecisionAsync,
                    OpenDateReviewFile);
            })
            .OrderBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
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

        DateReviewItems =
            BuildDateReviewItems(plan);

        Issues =
            plan.Issues
                .Where(issue => issue.Kind is not (
                    MediaSortIssueKind.MissingCaptureDate
                    or MediaSortIssueKind.DateReview
                    or MediaSortIssueKind.InsufficientDateConfidence
                    or MediaSortIssueKind.DateConflict))
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
            return $"Vorschau abgeschlossen: {plan.ProblemCount:N0} Problem(e). Es wird nichts ausgeführt.";
        }

        var details =
            new List<string>();

        if (plan.DateReviewCount > 0)
        {
            details.Add(
                $"{plan.DateReviewCount:N0} Datei(en) mit unklarem Aufnahmedatum");
        }

        if (plan.DateContextHintCount > 0)
        {
            details.Add(
                $"{plan.DateContextHintCount:N0} Serien-/Ordnerhinweis(e)");
        }

        if (details.Count == 0)
        {
            return $"Vorschau abgeschlossen: {plan.PlannedFileCount:N0} Datei(en) konfliktfrei geplant. "
                   + "Es wird nichts ausgeführt.";
        }

        return $"Vorschau abgeschlossen: {plan.PlannedFileCount:N0} Datei(en) geplant, "
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
