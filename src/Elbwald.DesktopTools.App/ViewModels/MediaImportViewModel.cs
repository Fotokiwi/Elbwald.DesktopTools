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
    private readonly INavigationService _navigationService;

    private MediaImportPlan? _plan;
    private MediaImportCleanupPlan? _cleanupPlan;
    private MediaImportExecutionResult? _lastImportResult;
    private CancellationTokenSource? _cancellationTokenSource;
    private Stopwatch? _stopwatch;

    [ObservableProperty] private string sourcePath = string.Empty;
    [ObservableProperty] private string destinationPath = string.Empty;
    [ObservableProperty] private string destinationStatus = "Import-Wartehalle wird geprüft …";
    [ObservableProperty] private string statusMessage = "Quelle auswählen und anschließend eine Importvorschau erstellen.";
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
    [ObservableProperty] private IReadOnlyList<string> cleanupIssues = Array.Empty<string>();

    public MediaImportViewModel(
        IMediaSourcePicker sourcePicker,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageResolver,
        IMediaImportPlanner importPlanner,
        IMediaImportExecutor importExecutor,
        IMediaImportCleanupPlanner cleanupPlanner,
        IMediaImportCleanupExecutor cleanupExecutor,
        INavigationService navigationService)
    {
        _sourcePicker = sourcePicker;
        _storageSettingsStore = storageSettingsStore;
        _storageResolver = storageResolver;
        _importPlanner = importPlanner;
        _importExecutor = importExecutor;
        _cleanupPlanner = cleanupPlanner;
        _cleanupExecutor = cleanupExecutor;
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

        _ = RefreshDestinationAsync();
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

    public bool HasSource => !string.IsNullOrWhiteSpace(SourcePath);
    public bool HasIssues => Issues.Count > 0;
    public bool HasPreviewItems => PreviewItems.Count > 0;
    public bool CanImport => _plan?.CanExecute == true && !IsBusy;
    public string ProgressText => CopyCount <= 0 ? "" : $"{CompletedCopyCount:N0} / {CopyCount:N0} Dateien";
    public bool HasCleanupIssues => CleanupIssues.Count > 0;
    public bool CleanupConfirmationMatches => string.Equals(CleanupConfirmationText.Trim(), "QUELLE LEEREN", StringComparison.Ordinal);

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
    }

    partial void OnSourcePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasSource));
        CreatePreviewCommand.NotifyCanExecuteChanged();
    }

    partial void OnDestinationAvailableChanged(bool value) => CreatePreviewCommand.NotifyCanExecuteChanged();
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

    private bool CanCreatePreview() => !IsBusy && DestinationAvailable && HasSource;
    private bool CanExecuteImport() => _plan?.CanExecute == true && !IsBusy;
    private bool CanPrepareCleanup() => CleanupAvailable && !IsBusy && _plan is not null;
    private bool CanConfirmCleanupWarning() => HasCleanupPlan && _cleanupPlan?.CanExecute == true && !IsBusy;
    private bool CanExecuteCleanup() => CleanupSecondConfirmationVisible && CleanupConfirmationMatches && _cleanupPlan?.CanExecute == true && !IsBusy;

    private async Task ChooseSourceAsync()
    {
        var selected = await _sourcePicker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        SourcePath = selected;
        ClearPlan();
        ClearCleanup();
        StatusMessage = "Quelle gewählt. Erstelle jetzt die Importvorschau.";
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
                DestinationPath = string.Empty;
                DestinationStatus = "Keine aktive Import-Wartehalle konfiguriert.";
                return;
            }

            var resolution = _storageResolver.Resolve(endpoint.Location);
            DestinationAvailable = resolution.IsAvailable;
            DestinationPath = resolution.ResolvedPath ?? string.Empty;
            DestinationStatus = resolution.IsAvailable
                ? $"{endpoint.Name} · über Volume-ID eindeutig verfügbar"
                : resolution.Message;
        }
        catch (Exception exception)
        {
            DestinationAvailable = false;
            DestinationPath = string.Empty;
            DestinationStatus = "Import-Wartehalle konnte nicht geladen werden: " + exception.GetBaseException().Message;
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
        ResultMessage = string.Empty;
        ClearCleanup();
        _cancellationTokenSource = new CancellationTokenSource();
        _stopwatch = Stopwatch.StartNew();
        StatusMessage = "Quelle wird vollständig geprüft …";

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
                ? "Die Vorschau ist unvollständig. Der Import bleibt aus Sicherheitsgründen gesperrt."
                : _plan.CopyCount == 0
                    ? "Keine neuen Dateien zu importieren. Vorhandene bitidentische Dateien werden nicht erneut kopiert."
                    : $"Vorschau fertig: {_plan.CopyCount:N0} Datei(en) werden kopiert, {_plan.AlreadyImportedCount:N0} bereits vorhanden, {_plan.ConflictCount:N0} Konflikt(e).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Vorschau abgebrochen.";
            ClearPlan();
        }
        catch (Exception exception)
        {
            StatusMessage = "Importvorschau fehlgeschlagen: " + exception.GetBaseException().Message;
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
        ResultMessage = string.Empty;
        CompletedCopyCount = 0;
        ProgressPercentage = 0;
        _cancellationTokenSource = new CancellationTokenSource();
        _stopwatch = Stopwatch.StartNew();
        StatusMessage = "Import läuft. Quellen bleiben unverändert.";

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
            ResultMessage = result.Message + $" Importiert: {result.CopiedCount:N0}; bereits vorhanden: {result.AlreadyImportedCount:N0}; Konflikte: {result.ConflictCount:N0}; offen: {result.RemainingCount:N0}.";
            HasResult = true;
            _lastImportResult = result;
            CleanupAvailable = result.State == MediaImportExecutionState.Completed
                               && result.CopiedCount == CopyCount
                               && result.CopiedCount > 0;
            StatusMessage = result.State == MediaImportExecutionState.Completed
                ? "Import abgeschlossen. Die Quelle wurde nicht verändert."
                : result.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Importabbruch angefordert. Die Quelle wird nicht verändert.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Import fehlgeschlagen: " + exception.GetBaseException().Message;
            ResultMessage = "Die Quelle wurde nicht gelöscht. Prüfe Protokoll und Zielzustand, bevor du erneut importierst.";
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
        CleanupStatus = "Quelle und Zielkopien werden erneut vollständig per SHA-256 geprüft …";
        CleanupResultMessage = string.Empty;
        HasCleanupResult = false;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            _cleanupPlan = await _cleanupPlanner.CreatePlanAsync(_plan, _cancellationTokenSource.Token);
            CleanupFileCount = _cleanupPlan.FileCount;
            CleanupSize = FormatBytes(_cleanupPlan.TotalBytes);
            CleanupIssues = _cleanupPlan.Issues
                .Select(issue => string.IsNullOrWhiteSpace(issue.Path) ? issue.Message : $"{issue.Message} · {issue.Path}")
                .ToArray();
            HasCleanupPlan = true;
            CleanupSecondConfirmationVisible = false;
            CleanupConfirmationText = string.Empty;
            CleanupStatus = _cleanupPlan.CanExecute
                ? $"{_cleanupPlan.FileCount:N0} Datei(en) sind für die Quellbereinigung verifiziert. Noch wurde nichts gelöscht."
                : "Quellbereinigung ist gesperrt. Mindestens eine Datei konnte nicht eindeutig verifiziert werden.";
        }
        catch (OperationCanceledException)
        {
            CleanupStatus = "Prüfung der Quellbereinigung abgebrochen. Es wurde nichts gelöscht.";
            _cleanupPlan = null;
            HasCleanupPlan = false;
        }
        catch (Exception exception)
        {
            CleanupStatus = "Quellbereinigung konnte nicht vorbereitet werden: " + exception.GetBaseException().Message;
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
        CleanupStatus = "Bestätigung 1 von 2 erfolgt. Tippe jetzt exakt QUELLE LEEREN ein.";
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
        CleanupStatus = "Finale Sicherheitsprüfung läuft. Vor dem ersten Löschen kann der Vorgang noch blockiert werden.";
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var result = await _cleanupExecutor.ExecuteAsync(
                _cleanupPlan,
                RemoveEmptyDirectories,
                _cancellationTokenSource.Token);

            CleanupResultMessage = result.Message
                + $" Entfernt: {result.DeletedFileCount:N0}/{result.PlannedFileCount:N0} Dateien; leere Unterordner: {result.RemovedDirectoryCount:N0}; Report-ID: {result.CorrelationId}.";
            HasCleanupResult = true;

            if (result.State == MediaImportCleanupExecutionState.Completed)
            {
                CleanupStatus = "Quellbereinigung abgeschlossen. Die verifizierten Zielkopien bleiben in der Import-Wartehalle erhalten.";
                CleanupAvailable = false;
                HasCleanupPlan = false;
                CleanupSecondConfirmationVisible = false;
                CleanupConfirmationText = string.Empty;
            }
            else
            {
                CleanupStatus = result.Message;
            }
        }
        catch (OperationCanceledException)
        {
            CleanupStatus = "Quellbereinigung wurde vor Beginn der destruktiven Phase abgebrochen. Es wurde nichts gelöscht.";
        }
        catch (Exception exception)
        {
            CleanupStatus = "Quellbereinigung fehlgeschlagen: " + exception.GetBaseException().Message;
            CleanupResultMessage = "Keine weiteren Löschversuche durchführen, bevor Quelle und Import-Wartehalle geprüft wurden.";
            HasCleanupResult = true;
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            IsBusy = false;
        }
    }

    private void CancelCleanup()
    {
        CleanupSecondConfirmationVisible = false;
        CleanupConfirmationText = string.Empty;
        CleanupStatus = HasCleanupPlan
            ? "Quellbereinigung nicht freigegeben. Es wurde nichts gelöscht."
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
