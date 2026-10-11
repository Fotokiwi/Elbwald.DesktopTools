using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MediaImportViewModel : ObservableObject
{
    private readonly IMediaSourcePicker _sourcePicker;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageResolver;
    private readonly IMediaImportPlanner _importPlanner;
    private readonly IMediaImportExecutor _importExecutor;
    private readonly IMediaImportCleanupPlanner _cleanupPlanner;
    private readonly IMediaImportCleanupExecutor _cleanupExecutor;
    private readonly IMediaAnalysisHistoryStore _analysisHistoryStore;
    private readonly IMediaImportHistoryStore _importHistoryStore;
    private readonly INavigationService _navigationService;

    private MediaImportPlan? _plan;
    private MediaImportCleanupPlan? _cleanupPlan;
    private MediaImportExecutionResult? _lastImportResult;
    private CancellationTokenSource? _cancellationTokenSource;
    private Stopwatch? _stopwatch;
    private bool _isRefreshingDefaults;
    private bool _sourceWasChosenManually;

    [ObservableProperty] private string sourcePath = string.Empty;
    [ObservableProperty] private string destinationRootPath = string.Empty;
    [ObservableProperty] private string destinationSubfolderName = string.Empty;
    [ObservableProperty] private string destinationPath = string.Empty;
    [ObservableProperty] private string destinationStatus = "Speicherort wird geprüft …";
    [ObservableProperty] private string destinationFolderStatus = "Ein passender Ordnername wird vorgeschlagen, sobald Quelle und Speicherort verfügbar sind.";
    [ObservableProperty] private string statusMessage = "Quelle und Speicherort prüfen und anschließend den Import vorbereiten.";
    [ObservableProperty] private string resultMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasPlan;
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private bool destinationAvailable;
    [ObservableProperty] private int fileCount;
    [ObservableProperty] private int copyCount;
    [ObservableProperty] private int alreadyImportedCount;
    [ObservableProperty] private int conflictCount;
    [ObservableProperty] private int issueCount;
    [ObservableProperty] private int completedCopyCount;
    [ObservableProperty] private int progressPercentage;
    [ObservableProperty] private string totalSize = "0 B";
    [ObservableProperty] private string copySize = "0 B";
    [ObservableProperty] private string elapsed = "00:00";
    [ObservableProperty] private IReadOnlyList<MediaImportPlanItemViewModel> previewItems = Array.Empty<MediaImportPlanItemViewModel>();
    [ObservableProperty] private IReadOnlyList<string> issues = Array.Empty<string>();
    [ObservableProperty] private bool cleanupAvailable;
    [ObservableProperty] private bool hasCleanupPlan;
    [ObservableProperty] private bool cleanupSecondConfirmationVisible;
    [ObservableProperty] private bool hasCleanupResult;
    [ObservableProperty] private bool removeEmptyDirectories = true;
    [ObservableProperty] private string cleanupConfirmationText = string.Empty;
    [ObservableProperty] private int cleanupFileCount;
    [ObservableProperty] private string cleanupSize = "0 B";
    [ObservableProperty] private string cleanupStatus = string.Empty;
    [ObservableProperty] private string cleanupResultMessage = string.Empty;
    [ObservableProperty] private string cleanupTechnicalDetails = string.Empty;
    [ObservableProperty] private string cleanupResultTitle = "Quelle aufräumen";
    [ObservableProperty] private int cleanupRetainedCount;
    [ObservableProperty] private bool importCompleted;
    [ObservableProperty] private IReadOnlyList<string> cleanupIssues = Array.Empty<string>();

    public MediaImportViewModel(
        IMediaSourcePicker sourcePicker,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageResolver,
        IMediaImportPlanner importPlanner,
        IMediaImportExecutor importExecutor,
        IMediaImportCleanupPlanner cleanupPlanner,
        IMediaImportCleanupExecutor cleanupExecutor,
        IMediaAnalysisHistoryStore analysisHistoryStore,
        IMediaImportHistoryStore importHistoryStore,
        INavigationService navigationService)
    {
        _sourcePicker = sourcePicker;
        _storageSettingsStore = storageSettingsStore;
        _storageResolver = storageResolver;
        _importPlanner = importPlanner;
        _importExecutor = importExecutor;
        _cleanupPlanner = cleanupPlanner;
        _cleanupExecutor = cleanupExecutor;
        _analysisHistoryStore = analysisHistoryStore;
        _importHistoryStore = importHistoryStore;
        _navigationService = navigationService;

        ChooseSourceCommand = new AsyncRelayCommand(ChooseSourceAsync, () => !IsBusy);
        CreatePreviewCommand = new AsyncRelayCommand(CreatePreviewAsync, CanCreatePreview);
        ExecuteImportCommand = new AsyncRelayCommand(ExecuteImportAsync, CanExecuteImport);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        OpenSettingsCommand = new RelayCommand(() => _navigationService.NavigateTo("settings"));
        PrepareCleanupCommand = new AsyncRelayCommand(PrepareCleanupAsync, CanPrepareCleanup);
        ConfirmCleanupWarningCommand = new RelayCommand(ConfirmCleanupWarning, CanConfirmCleanupWarning);
        ExecuteCleanupCommand = new AsyncRelayCommand(ExecuteCleanupAsync, CanExecuteCleanup);
        CancelCleanupCommand = new RelayCommand(CancelCleanup);
        OpenImportedFolderCommand = new RelayCommand(OpenImportedFolder, CanOpenImportedFolder);
        OpenLibraryCommand = new RelayCommand(() => _navigationService.NavigateTo("library"));

    }

    public IAsyncRelayCommand ChooseSourceCommand { get; }
    public IAsyncRelayCommand CreatePreviewCommand { get; }
    public IAsyncRelayCommand ExecuteImportCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IAsyncRelayCommand PrepareCleanupCommand { get; }
    public IRelayCommand ConfirmCleanupWarningCommand { get; }
    public IAsyncRelayCommand ExecuteCleanupCommand { get; }
    public IRelayCommand CancelCleanupCommand { get; }
    public IRelayCommand OpenImportedFolderCommand { get; }
    public IRelayCommand OpenLibraryCommand { get; }

    public bool HasSource => !string.IsNullOrWhiteSpace(SourcePath);
    public bool SourceAvailable => HasSource && Directory.Exists(SourcePath);
    public bool HasValidDestination => DestinationAvailable && !string.IsNullOrWhiteSpace(DestinationPath);
    public bool HasIssues => Issues.Count > 0;
    public bool HasPreviewItems => PreviewItems.Count > 0;
    public bool CanImport => _plan?.CanExecute == true && !IsBusy;
    public string ProgressText => CopyCount <= 0 ? "" : $"{CompletedCopyCount:N0} / {CopyCount:N0} Dateien";
    public bool HasCleanupIssues => CleanupIssues.Count > 0;
    public bool CleanupConfirmationMatches => string.Equals(CleanupConfirmationText.Trim(), "LÖSCHEN", StringComparison.Ordinal);

    partial void OnIsBusyChanged(bool value)
    {
        ChooseSourceCommand.NotifyCanExecuteChanged();
        CreatePreviewCommand.NotifyCanExecuteChanged();
        ExecuteImportCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanImport));
        PrepareCleanupCommand.NotifyCanExecuteChanged();
        ConfirmCleanupWarningCommand.NotifyCanExecuteChanged();
        ExecuteCleanupCommand.NotifyCanExecuteChanged();
        OpenImportedFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnSourcePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(SourceAvailable));
        CreatePreviewCommand.NotifyCanExecuteChanged();
    }

    partial void OnDestinationAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(HasValidDestination));
        CreatePreviewCommand.NotifyCanExecuteChanged();
    }

    partial void OnDestinationSubfolderNameChanged(string value)
    {
        UpdateDestinationPath();
        ClearPlan();
        ClearCleanup();
    }

    partial void OnDestinationPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasValidDestination));
        CreatePreviewCommand.NotifyCanExecuteChanged();
        OpenImportedFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnImportCompletedChanged(bool value) => OpenImportedFolderCommand.NotifyCanExecuteChanged();
    partial void OnIssuesChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasIssues));
    partial void OnPreviewItemsChanged(IReadOnlyList<MediaImportPlanItemViewModel> value) => OnPropertyChanged(nameof(HasPreviewItems));
    partial void OnCompletedCopyCountChanged(int value) => OnPropertyChanged(nameof(ProgressText));
    partial void OnCopyCountChanged(int value) => OnPropertyChanged(nameof(ProgressText));
    partial void OnCleanupAvailableChanged(bool value) => PrepareCleanupCommand.NotifyCanExecuteChanged();
    partial void OnHasCleanupPlanChanged(bool value) => ConfirmCleanupWarningCommand.NotifyCanExecuteChanged();
    partial void OnCleanupSecondConfirmationVisibleChanged(bool value) => ExecuteCleanupCommand.NotifyCanExecuteChanged();
    partial void OnCleanupConfirmationTextChanged(string value)
    {
        OnPropertyChanged(nameof(CleanupConfirmationMatches));
        ExecuteCleanupCommand.NotifyCanExecuteChanged();
    }
    partial void OnCleanupIssuesChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasCleanupIssues));

    private bool CanCreatePreview() => !IsBusy && SourceAvailable && HasValidDestination;
    private bool CanExecuteImport() => _plan?.CanExecute == true && !IsBusy;
    private bool CanPrepareCleanup() => CleanupAvailable && !IsBusy && _plan is not null;
    private bool CanConfirmCleanupWarning() => HasCleanupPlan && _cleanupPlan?.CanExecute == true && !IsBusy;
    private bool CanExecuteCleanup() => CleanupSecondConfirmationVisible && CleanupConfirmationMatches && _cleanupPlan?.CanExecute == true && !IsBusy;
    private bool CanOpenImportedFolder() => ImportCompleted && !IsBusy && !string.IsNullOrWhiteSpace(DestinationPath) && Directory.Exists(DestinationPath);

    private async Task ChooseSourceAsync()
    {
        var selected = await _sourcePicker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        _sourceWasChosenManually = true;
        SourcePath = selected;
        SuggestDestinationSubfolder();
        ClearPlan();
        ClearCleanup();
        StatusMessage = "Quelle gewählt. Der vorgeschlagene Ordnername kann vor dem Import noch geändert werden.";
    }

    public async Task RefreshDefaultsAsync()
    {
        if (_isRefreshingDefaults
            || IsBusy
            || HasPlan
            || HasResult
            || HasCleanupPlan)
        {
            return;
        }

        _isRefreshingDefaults = true;

        try
        {
            var sourceChanged = false;

            if (!_sourceWasChosenManually)
            {
                try
                {
                    var lastAnalysis = await _analysisHistoryStore.LoadLastAsync();
                    if (lastAnalysis is not null && lastAnalysis.IsFolder)
                    {
                        sourceChanged = !PathsEqual(SourcePath, lastAnalysis.SourcePath);
                        SourcePath = lastAnalysis.SourcePath;
                        StatusMessage = Directory.Exists(lastAnalysis.SourcePath)
                            ? $"Letzte Analyse vom {lastAnalysis.CompletedUtc.ToLocalTime():dd.MM.yyyy, HH:mm} Uhr als Importquelle übernommen."
                            : "Der Pfad der letzten Analyse wurde übernommen, ist derzeit aber nicht erreichbar. Wähle bei Bedarf eine andere Quelle.";
                    }
                    else if (lastAnalysis is not null && !HasSource)
                    {
                        StatusMessage = "Die letzte Analyse betraf eine Einzeldatei. Für den Import bitte einen Quellordner auswählen.";
                    }
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or NotSupportedException
                        or ArgumentException)
                {
                    Console.Error.WriteLine($"Letzte Analyse konnte für den Import nicht geladen werden: {exception.Message}");
                }
            }

            await RefreshDestinationAsync();

            if (HasSource
                && DestinationAvailable
                && (sourceChanged || string.IsNullOrWhiteSpace(DestinationSubfolderName)))
            {
                SuggestDestinationSubfolder();
            }

            OnPropertyChanged(nameof(SourceAvailable));
            CreatePreviewCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            _isRefreshingDefaults = false;
        }
    }

    private async Task RefreshDestinationAsync()
    {
        try
        {
            var settings = await _storageSettingsStore.LoadAsync();
            var endpoint = settings.Find(StorageEndpoint.DefaultImportId);
            if (endpoint is null || !endpoint.Enabled)
            {
                DestinationAvailable = false;
                DestinationRootPath = string.Empty;
                DestinationPath = string.Empty;
                DestinationStatus = "Es ist noch kein Import-Speicherort eingerichtet.";
                DestinationFolderStatus = "Lege zuerst unter Einstellungen einen Speicherort für Importe fest.";
                return;
            }

            var resolution = _storageResolver.Resolve(endpoint.Location);
            DestinationAvailable = resolution.IsAvailable;
            DestinationRootPath = resolution.ResolvedPath ?? string.Empty;
            DestinationStatus = resolution.IsAvailable
                ? $"{endpoint.Name} · Speicherort bereit"
                : resolution.Message;

            UpdateDestinationPath();
        }
        catch (Exception exception)
        {
            DestinationAvailable = false;
            DestinationRootPath = string.Empty;
            DestinationPath = string.Empty;
            DestinationStatus = "Der Import-Speicherort konnte nicht geladen werden: " + exception.GetBaseException().Message;
            DestinationFolderStatus = "Ziel kann derzeit nicht berechnet werden.";
        }
    }

    private async Task CreatePreviewAsync()
    {
        if (!CanCreatePreview())
        {
            return;
        }

        IsBusy = true;
        HasResult = false;
        ImportCompleted = false;
        ResultMessage = string.Empty;
        ClearCleanup();
        _cancellationTokenSource = new CancellationTokenSource();
        _stopwatch = Stopwatch.StartNew();
        StatusMessage = "Dateien werden geprüft. Noch wird nichts kopiert …";

        try
        {
            _plan = await _importPlanner.CreatePlanAsync(
                SourcePath,
                DestinationPath,
                _cancellationTokenSource.Token);

            FileCount = _plan.FileCount;
            CopyCount = _plan.CopyCount;
            AlreadyImportedCount = _plan.AlreadyImportedCount;
            ConflictCount = _plan.ConflictCount;
            IssueCount = _plan.Issues.Count;
            TotalSize = FormatBytes(_plan.TotalBytes);
            CopySize = FormatBytes(_plan.CopyBytes);
            PreviewItems = _plan.Items
                .Take(300)
                .Select(item => new MediaImportPlanItemViewModel(item))
                .ToArray();
            Issues = _plan.Issues
                .Select(issue => string.IsNullOrWhiteSpace(issue.Path) ? issue.Message : $"{issue.Message} · {issue.Path}")
                .ToArray();
            HasPlan = true;

            StatusMessage = !_plan.IsComplete
                ? "Die Prüfung ist noch nicht vollständig. Der Import bleibt gesperrt, damit keine Dateien übersehen werden."
                : _plan.CopyCount == 0
                    ? "Es gibt keine neuen Dateien zu importieren. Bereits vorhandene Dateien werden nicht doppelt kopiert."
                    : $"Alles bereit: {_plan.CopyCount:N0} neue Datei(en) werden kopiert. {_plan.AlreadyImportedCount:N0} sind bereits vorhanden, {_plan.ConflictCount:N0} Namenskonflikt(e) wurden erkannt.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Prüfung abgebrochen. Es wurde nichts kopiert.";
            ClearPlan();
        }
        catch (Exception exception)
        {
            StatusMessage = "Die Importprüfung ist fehlgeschlagen: " + exception.GetBaseException().Message;
            ClearPlan();
        }
        finally
        {
            StopTimer();
            IsBusy = false;
            ExecuteImportCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ExecuteImportAsync()
    {
        if (_plan is null || !CanExecuteImport())
        {
            return;
        }

        IsBusy = true;
        HasResult = false;
        ImportCompleted = false;
        ResultMessage = string.Empty;
        CompletedCopyCount = 0;
        ProgressPercentage = 0;
        _cancellationTokenSource = new CancellationTokenSource();
        _stopwatch = Stopwatch.StartNew();
        StatusMessage = "Import läuft. Deine Originaldateien bleiben unverändert.";

        var timerTask = UpdateElapsedAsync(_cancellationTokenSource.Token);
        try
        {
            var progress = new Progress<int>(completed =>
            {
                CompletedCopyCount = completed;
                ProgressPercentage = CopyCount <= 0 ? 100 : (int)Math.Round(completed * 100d / CopyCount);
            });

            var result = await _importExecutor.ExecuteAsync(
                _plan,
                progress,
                _cancellationTokenSource.Token);

            CompletedCopyCount = result.CopiedCount;
            ProgressPercentage = CopyCount <= 0 ? 100 : (int)Math.Round(result.CopiedCount * 100d / CopyCount);
            ResultMessage = result.Message + $" Neu kopiert: {result.CopiedCount:N0}; bereits vorhanden: {result.AlreadyImportedCount:N0}; Namenskonflikte: {result.ConflictCount:N0}; noch offen: {result.RemainingCount:N0}.";
            HasResult = true;
            _lastImportResult = result;
            CleanupAvailable = result.State == MediaImportExecutionState.Completed
                               && result.CopiedCount == CopyCount
                               && result.CopiedCount > 0;
            StatusMessage = result.State == MediaImportExecutionState.Completed
                ? "Import abgeschlossen. Deine Originaldateien wurden nicht verändert."
                : result.Message;

            if (result.State == MediaImportExecutionState.Completed)
            {
                ImportCompleted = true;
                OpenImportedFolderCommand.NotifyCanExecuteChanged();
                await SaveImportHistorySafeAsync(result);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Importabbruch angefordert. Deine Originaldateien werden nicht verändert.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Import fehlgeschlagen: " + exception.GetBaseException().Message;
            ResultMessage = "Deine Originaldateien wurden nicht gelöscht. Prüfe den Speicherort, bevor du den Import erneut startest.";
            HasResult = true;
        }
        finally
        {
            _cancellationTokenSource?.Cancel();
            try { await timerTask; } catch (OperationCanceledException) { }
            StopTimer();
            IsBusy = false;
        }
    }

    private async Task PrepareCleanupAsync()
    {
        if (_plan is null || _lastImportResult is null || !CanPrepareCleanup())
        {
            return;
        }

        IsBusy = true;
        CleanupStatus = "Die importierten Kopien werden noch einmal geprüft. Noch wird nichts gelöscht …";
        CleanupResultMessage = string.Empty;
        CleanupTechnicalDetails = string.Empty;
        CleanupResultTitle = "Quelle aufräumen";
        HasCleanupResult = false;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            _cleanupPlan = await _cleanupPlanner.CreatePlanAsync(_plan, _cancellationTokenSource.Token);
            CleanupFileCount = _cleanupPlan.FileCount;
            CleanupRetainedCount = Math.Max(0, FileCount - _cleanupPlan.FileCount);
            CleanupSize = FormatBytes(_cleanupPlan.TotalBytes);
            CleanupIssues = _cleanupPlan.Issues
                .Select(issue => string.IsNullOrWhiteSpace(issue.Path) ? issue.Message : $"{issue.Message} · {issue.Path}")
                .ToArray();
            HasCleanupPlan = true;
            CleanupSecondConfirmationVisible = false;
            CleanupConfirmationText = string.Empty;
            CleanupStatus = _cleanupPlan.CanExecute
                ? $"{_cleanupPlan.FileCount:N0} Datei(en) können sicher von der ursprünglichen Quelle entfernt werden. Noch wurde nichts gelöscht."
                : "Es kann noch nichts entfernt werden. Mindestens eine Kopie konnte nicht eindeutig geprüft werden.";
        }
        catch (OperationCanceledException)
        {
            CleanupStatus = "Prüfung abgebrochen. Es wurde nichts gelöscht.";
            _cleanupPlan = null;
            HasCleanupPlan = false;
        }
        catch (Exception exception)
        {
            CleanupStatus = "Die Quelle konnte nicht sicher geprüft werden: " + exception.GetBaseException().Message;
            _cleanupPlan = null;
            HasCleanupPlan = false;
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            IsBusy = false;
        }
    }

    private void ConfirmCleanupWarning()
    {
        if (!CanConfirmCleanupWarning())
        {
            return;
        }

        CleanupSecondConfirmationVisible = true;
        CleanupConfirmationText = string.Empty;
        CleanupStatus = $"Letzte Bestätigung: Tippe LÖSCHEN ein, um {CleanupFileCount:N0} geprüfte Originaldatei(en) von der Quelle zu entfernen.";
    }

    private async Task ExecuteCleanupAsync()
    {
        if (_cleanupPlan is null || !CanExecuteCleanup())
        {
            return;
        }

        IsBusy = true;
        HasCleanupResult = false;
        CleanupResultMessage = string.Empty;
        CleanupStatus = "Letzte Sicherheitsprüfung läuft. Wenn etwas nicht eindeutig stimmt, wird nichts gelöscht.";
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var result = await _cleanupExecutor.ExecuteAsync(
                _cleanupPlan,
                RemoveEmptyDirectories,
                _cancellationTokenSource.Token);

            CleanupTechnicalDetails = $"Report-ID: {result.CorrelationId} · Geplant: {result.PlannedFileCount:N0} · Entfernt: {result.DeletedFileCount:N0} · Leere Unterordner entfernt: {result.RemovedDirectoryCount:N0}.";
            HasCleanupResult = true;

            if (result.State == MediaImportCleanupExecutionState.Completed)
            {
                CleanupResultTitle = "Quelle aufgeräumt";
                CleanupResultMessage = $"{result.DeletedFileCount:N0} Datei(en) wurden von der ursprünglichen Quelle entfernt. {CleanupSize} wurden freigegeben. Deine importierten Kopien bleiben unverändert erhalten.";
                CleanupStatus = "Quelle aufgeräumt. Die geprüften Kopien im Importordner bleiben erhalten.";
                CleanupAvailable = false;
                HasCleanupPlan = false;
                CleanupSecondConfirmationVisible = false;
                CleanupConfirmationText = string.Empty;
            }
            else
            {
                CleanupResultTitle = "Quelle konnte nicht vollständig aufgeräumt werden";
                CleanupResultMessage = result.Message;
                CleanupStatus = result.Message;
            }
        }
        catch (OperationCanceledException)
        {
            CleanupStatus = "Aufräumen wurde vor dem Löschen abgebrochen. Es wurde nichts gelöscht.";
        }
        catch (Exception exception)
        {
            CleanupStatus = "Die Quelle konnte nicht sicher aufgeräumt werden: " + exception.GetBaseException().Message;
            CleanupResultTitle = "Quelle konnte nicht aufgeräumt werden";
            CleanupResultMessage = "Es wurden keine weiteren Löschversuche gestartet. Prüfe Quelle und importierte Kopien, bevor du es erneut versuchst.";
            CleanupTechnicalDetails = exception.GetBaseException().Message;
            HasCleanupResult = true;
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            IsBusy = false;
        }
    }

    private void SuggestDestinationSubfolder()
    {
        if (!HasSource)
        {
            return;
        }

        var sourceName = GetSourceDisplayName(SourcePath);
        var descriptiveName = IsGenericSourceFolderName(sourceName)
            ? "Import"
            : sourceName;

        descriptiveName = SanitizeFolderName(descriptiveName);
        if (string.IsNullOrWhiteSpace(descriptiveName))
        {
            descriptiveName = "Import";
        }

        var baseName = $"{DateTimeOffset.Now:yyyy-MM-dd} - {descriptiveName}";
        DestinationSubfolderName = BuildConflictFreeSubfolderName(baseName);
    }

    private string BuildConflictFreeSubfolderName(string baseName)
    {
        if (!DestinationAvailable || string.IsNullOrWhiteSpace(DestinationRootPath))
        {
            return baseName;
        }

        var candidate = baseName;
        var suffix = 2;

        while (PathExistsUnderDestinationRoot(candidate))
        {
            candidate = $"{baseName} ({suffix})";
            suffix++;
        }

        return candidate;
    }

    private bool PathExistsUnderDestinationRoot(string subfolderName)
    {
        try
        {
            var path = Path.Combine(DestinationRootPath, subfolderName);
            return Directory.Exists(path) || File.Exists(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return true;
        }
    }

    private void UpdateDestinationPath()
    {
        if (!DestinationAvailable || string.IsNullOrWhiteSpace(DestinationRootPath))
        {
            DestinationPath = string.Empty;
            DestinationFolderStatus = "Import-Stammverzeichnis ist nicht verfügbar.";
            return;
        }

        if (!HasSource && string.IsNullOrWhiteSpace(DestinationSubfolderName))
        {
            DestinationPath = string.Empty;
            DestinationFolderStatus = "Der Unterordner wird nach Auswahl einer Quelle automatisch vorgeschlagen.";
            return;
        }

        var validationError = ValidateSubfolderName(DestinationSubfolderName);
        if (validationError is not null)
        {
            DestinationPath = string.Empty;
            DestinationFolderStatus = validationError;
            return;
        }

        try
        {
            var destination = Path.GetFullPath(
                Path.Combine(DestinationRootPath, DestinationSubfolderName.Trim()));

            var root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(DestinationRootPath));

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            {
                DestinationPath = string.Empty;
                DestinationFolderStatus = "Der Unterordner muss direkt innerhalb des Import-Stammverzeichnisses liegen.";
                return;
            }

            if (File.Exists(destination))
            {
                DestinationPath = string.Empty;
                DestinationFolderStatus = "Am berechneten Ziel existiert bereits eine Datei mit diesem Namen.";
                return;
            }

            DestinationPath = destination;
            DestinationFolderStatus = Directory.Exists(destination)
                ? "Der Zielordner existiert bereits. Die Vorschau vergleicht vorhandene Dateien per SHA-256 und überschreibt nichts."
                : "Neuer Zielordner. Er wird erst beim tatsächlichen Import angelegt.";
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            DestinationPath = string.Empty;
            DestinationFolderStatus = "Der Zielpfad ist ungültig: " + exception.Message;
        }
    }

    private static string? ValidateSubfolderName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Gib einen Namen für den Import-Unterordner an.";
        }

        var trimmed = value.Trim();
        if (!string.Equals(trimmed, value, StringComparison.Ordinal))
        {
            return "Der Unterordner darf nicht mit Leerzeichen beginnen oder enden.";
        }

        if (trimmed is "." or "..")
        {
            return "Dieser Unterordnername ist nicht zulässig.";
        }

        if (trimmed.Length > 120)
        {
            return "Der Unterordnername ist zu lang. Maximal 120 Zeichen sind vorgesehen.";
        }

        if (trimmed.IndexOf('/') >= 0 || trimmed.IndexOf('\\') >= 0)
        {
            return "Der Name darf keine weiteren Pfadbestandteile enthalten.";
        }

        var invalid = Path.GetInvalidFileNameChars();
        if (trimmed.IndexOfAny(invalid) >= 0
            || trimmed.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0)
        {
            return "Der Unterordnername enthält Zeichen, die nicht plattformübergreifend zulässig sind.";
        }

        if (trimmed.EndsWith(".", StringComparison.Ordinal))
        {
            return "Der Unterordnername darf nicht mit einem Punkt enden.";
        }

        return null;
    }

    private static bool PathsEqual(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
                comparison);
        }
        catch
        {
            return string.Equals(first, second, StringComparison.Ordinal);
        }
    }

    private static string GetSourceDisplayName(string sourcePath)
    {
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(sourcePath));

            var name = Path.GetFileName(normalized);
            return string.IsNullOrWhiteSpace(name)
                ? "Import"
                : name;
        }
        catch
        {
            return "Import";
        }
    }

    private static bool IsGenericSourceFolderName(string value)
    {
        var name = value.Trim();
        return name.Equals("DCIM", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Camera", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Pictures", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Photos", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Images", StringComparison.OrdinalIgnoreCase)
               || name.Equals("PRIVATE", StringComparison.OrdinalIgnoreCase)
               || name.Equals("100MEDIA", StringComparison.OrdinalIgnoreCase)
               || name.Equals("100CANON", StringComparison.OrdinalIgnoreCase)
               || name.Equals("100NIKON", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFolderName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars()
            .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
            .ToHashSet();

        var sanitized = new string(
                value.Select(character => invalid.Contains(character) ? ' ' : character)
                    .ToArray())
            .Trim()
            .TrimEnd('.');

        while (sanitized.Contains("  ", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return sanitized.Length <= 80
            ? sanitized
            : sanitized[..80].TrimEnd();
    }

    private async Task SaveImportHistorySafeAsync(
        MediaImportExecutionResult result)
    {
        try
        {
            await _importHistoryStore.SaveLastAsync(
                new MediaImportHistoryEntry(
                    SourcePath: SourcePath,
                    DestinationRootPath: DestinationRootPath,
                    DestinationPath: DestinationPath,
                    DestinationSubfolderName: DestinationSubfolderName,
                    CompletedUtc: DateTimeOffset.UtcNow,
                    CopiedCount: result.CopiedCount,
                    AlreadyImportedCount: result.AlreadyImportedCount,
                    ConflictCount: result.ConflictCount));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException
                or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(
                $"Importverlauf konnte nicht gespeichert werden: {exception.Message}");
        }
    }

    private void OpenImportedFolder()
    {
        if (!CanOpenImportedFolder())
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DestinationPath,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusMessage = "Der importierte Ordner konnte nicht automatisch geöffnet werden: " + exception.Message;
        }
    }

    private void CancelCleanup()
    {
        CleanupSecondConfirmationVisible = false;
        CleanupConfirmationText = string.Empty;
        CleanupStatus = HasCleanupPlan
            ? "Aufräumen nicht bestätigt. Es wurde nichts gelöscht."
            : string.Empty;
    }

    private void Cancel() => _cancellationTokenSource?.Cancel();

    private async Task UpdateElapsedAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Elapsed = FormatElapsed(_stopwatch?.Elapsed ?? TimeSpan.Zero);
            await Task.Delay(250, cancellationToken);
        }
    }

    private void StopTimer()
    {
        if (_stopwatch is not null)
        {
            _stopwatch.Stop();
            Elapsed = FormatElapsed(_stopwatch.Elapsed);
        }
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void ClearPlan()
    {
        _plan = null;
        _lastImportResult = null;
        HasPlan = false;
        HasResult = false;
        ImportCompleted = false;
        ResultMessage = string.Empty;
        PreviewItems = Array.Empty<MediaImportPlanItemViewModel>();
        Issues = Array.Empty<string>();
        FileCount = 0;
        CopyCount = 0;
        AlreadyImportedCount = 0;
        ConflictCount = 0;
        IssueCount = 0;
        TotalSize = "0 B";
        CopySize = "0 B";
        ExecuteImportCommand.NotifyCanExecuteChanged();
    }

    private void ClearCleanup()
    {
        _cleanupPlan = null;
        _lastImportResult = null;
        CleanupAvailable = false;
        HasCleanupPlan = false;
        CleanupSecondConfirmationVisible = false;
        HasCleanupResult = false;
        CleanupConfirmationText = string.Empty;
        CleanupFileCount = 0;
        CleanupSize = "0 B";
        CleanupStatus = string.Empty;
        CleanupResultMessage = string.Empty;
        CleanupTechnicalDetails = string.Empty;
        CleanupResultTitle = "Quelle aufräumen";
        CleanupRetainedCount = 0;
        CleanupIssues = Array.Empty<string>();
        PrepareCleanupCommand.NotifyCanExecuteChanged();
        ConfirmCleanupWarningCommand.NotifyCanExecuteChanged();
        ExecuteCleanupCommand.NotifyCanExecuteChanged();
    }

    private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1
        ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
        : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
