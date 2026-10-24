using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.DuplicateFinding;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class DuplicateFinderViewModel : ObservableObject
{
    private readonly IDuplicateFinderService _duplicateFinderService;
    private readonly IDuplicateCleanupPlanner _duplicateCleanupPlanner;
    private readonly IDuplicateCleanupExecutor _duplicateCleanupExecutor;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageLocationResolver;
    private readonly IFileManagerService _fileManagerService;
    private readonly INavigationService _navigationService;
    private bool _suppressPlanChangedNotifications;
    private CancellationTokenSource? _batchCancellationSource;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isPreflightBusy;

    [ObservableProperty]
    private bool isExecutionBusy;

    [ObservableProperty]
    private bool cleanupConfirmationAccepted;

    [ObservableProperty]
    private bool batchCleanupConfirmationAccepted;

    [ObservableProperty]
    private bool isBatchOperationBusy;

    [ObservableProperty]
    private string batchOperationMessage = "Sehr sichere Gruppen können gesammelt geplant und geprüft werden; unklare Gruppen bleiben außen vor.";

    [ObservableProperty]
    private string statusMessage = "Dubletten werden aus dem Medienindex geladen …";

    [ObservableProperty]
    private string lastScanMessage = "Noch keine Dublettenprüfung ausgeführt.";

    [ObservableProperty]
    private string indexLastUpdatedMessage = "Indexstand wird geladen …";

    [ObservableProperty]
    private string preflightOperationMessage = "Wähle für eine Gruppe die Referenz und die Entfernungskandidaten und starte anschließend den Preflight.";

    [ObservableProperty]
    private string executionOperationMessage = "Nach bestandenem Preflight muss die Papierkorb-Aktion noch ausdrücklich bestätigt werden.";

    [ObservableProperty]
    private string lastExecutionSummary = "Noch keine Dublettenbereinigung ausgeführt.";

    [ObservableProperty]
    private IReadOnlyList<DuplicateCleanupExecutionItemViewModel> lastExecutionItems =
        Array.Empty<DuplicateCleanupExecutionItemViewModel>();

    [ObservableProperty]
    private int presentLocationCount;

    [ObservableProperty]
    private int duplicateGroupCount;

    [ObservableProperty]
    private int duplicateLocationCount;

    [ObservableProperty]
    private int redundantCopyCount;

    [ObservableProperty]
    private long reclaimableBytes;

    [ObservableProperty]
    private IReadOnlyList<DuplicateFinderGroupItemViewModel> groups =
        Array.Empty<DuplicateFinderGroupItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<DuplicateFinderGroupItemViewModel> filteredGroups =
        Array.Empty<DuplicateFinderGroupItemViewModel>();

    [ObservableProperty]
    private DuplicateFinderGroupItemViewModel? selectedGroup;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string selectedMediaType = "Alle";

    [ObservableProperty]
    private string selectedConfidence = "Alle Sicherheitsstufen";

    [ObservableProperty]
    private string selectedSort = "Speichergewinn ↓";

    [ObservableProperty]
    private IReadOnlyList<DuplicateFinderEndpointOptionViewModel> preferredEndpointOptions =
        Array.Empty<DuplicateFinderEndpointOptionViewModel>();

    [ObservableProperty]
    private DuplicateFinderEndpointOptionViewModel? selectedPreferredEndpoint;

    public DuplicateFinderViewModel(
        IDuplicateFinderService duplicateFinderService,
        IDuplicateCleanupPlanner duplicateCleanupPlanner,
        IDuplicateCleanupExecutor duplicateCleanupExecutor,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver,
        IFileManagerService fileManagerService,
        INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(duplicateFinderService);
        ArgumentNullException.ThrowIfNull(duplicateCleanupPlanner);
        ArgumentNullException.ThrowIfNull(duplicateCleanupExecutor);
        ArgumentNullException.ThrowIfNull(storageSettingsStore);
        ArgumentNullException.ThrowIfNull(storageLocationResolver);
        ArgumentNullException.ThrowIfNull(fileManagerService);
        ArgumentNullException.ThrowIfNull(navigationService);

        _duplicateFinderService = duplicateFinderService;
        _duplicateCleanupPlanner = duplicateCleanupPlanner;
        _duplicateCleanupExecutor = duplicateCleanupExecutor;
        _storageSettingsStore = storageSettingsStore;
        _storageLocationResolver = storageLocationResolver;
        _fileManagerService = fileManagerService;
        _navigationService = navigationService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
        RunPreflightCommand = new AsyncRelayCommand(RunPreflightAsync, CanRunPreflight);
        ExecuteCleanupCommand = new AsyncRelayCommand(ExecuteCleanupAsync, CanExecuteCleanup);
        KeepOldestModifiedCommand = new RelayCommand(KeepOldestModified, CanApplySelectedGroupRule);
        KeepNewestModifiedCommand = new RelayCommand(KeepNewestModified, CanApplySelectedGroupRule);
        ResetSelectedPlanCommand = new RelayCommand(ResetSelectedPlan, CanApplySelectedGroupRule);
        ApplyPreferredEndpointToFilteredCommand = new RelayCommand(ApplyPreferredEndpointToFiltered, CanApplyPreferredEndpointRule);
        ApplyVerySafeSuggestionsCommand = new RelayCommand(ApplyVerySafeSuggestions, CanApplyVerySafeSuggestions);
        RunSafeBatchPreflightCommand = new AsyncRelayCommand(RunSafeBatchPreflightAsync, CanRunSafeBatchPreflight);
        ExecuteSafeBatchCleanupCommand = new AsyncRelayCommand(ExecuteSafeBatchCleanupAsync, CanExecuteSafeBatchCleanup);
        CancelBatchOperationCommand = new RelayCommand(CancelBatchOperation, CanCancelBatchOperation);
        OpenMediaIndexCommand = new RelayCommand(() => _navigationService.NavigateTo("media-index"));
        OpenLibraryHealthCommand = new RelayCommand(() => _navigationService.NavigateTo("library-health"));

        _ = RefreshAsync();
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand RunPreflightCommand { get; }

    public IAsyncRelayCommand ExecuteCleanupCommand { get; }

    public IRelayCommand KeepOldestModifiedCommand { get; }

    public IRelayCommand KeepNewestModifiedCommand { get; }

    public IRelayCommand ResetSelectedPlanCommand { get; }

    public IRelayCommand ApplyPreferredEndpointToFilteredCommand { get; }

    public IRelayCommand ApplyVerySafeSuggestionsCommand { get; }

    public IAsyncRelayCommand RunSafeBatchPreflightCommand { get; }

    public IAsyncRelayCommand ExecuteSafeBatchCleanupCommand { get; }

    public IRelayCommand CancelBatchOperationCommand { get; }

    public IRelayCommand OpenMediaIndexCommand { get; }

    public IRelayCommand OpenLibraryHealthCommand { get; }

    public IReadOnlyList<string> MediaTypeFilters { get; } =
        ["Alle", "Bilder", "Videos", "Audio", "Unbekannt"];

    public IReadOnlyList<string> ConfidenceFilters { get; } =
        ["Alle Sicherheitsstufen", "Sehr sicher", "Wahrscheinlich", "Unklar"];

    public IReadOnlyList<string> SortOptions { get; } =
        ["Speichergewinn ↓", "Sicherheit + Speichergewinn", "Dateigröße ↓", "Kopienzahl ↓", "Dateityp + Name", "Dateiname A–Z"];

    public string ReclaimableText => MediaIndexBrowseItemViewModel.FormatBytes(ReclaimableBytes);

    public int PlannedRemovalCount => Groups.Sum(group => group.PlannedRemovalCount);

    public long PlannedReclaimableBytes => Groups.Sum(group => group.PlannedReclaimableBytes);

    public string PlannedReclaimableText =>
        MediaIndexBrowseItemViewModel.FormatBytes(PlannedReclaimableBytes);

    public string PlannedSummary =>
        Groups.Count == 0
            ? "Noch kein Bereinigungsplan"
            : $"{PlannedRemovalCount:N0} Kandidat(en) · {PlannedReclaimableText} im aktuellen Plan";

    public string DuplicateSummary =>
        DuplicateGroupCount == 0
            ? "Keine bitidentischen Dubletten gefunden"
            : DuplicateGroupCount == 1
                ? $"1 Dublettengruppe · {RedundantCopyCount:N0} zusätzliche Kopie(n)"
                : $"{DuplicateGroupCount:N0} Dublettengruppen · {RedundantCopyCount:N0} zusätzliche Kopie(n)";

    public string FilterSummary =>
        Groups.Count == 0
            ? "Keine Dublettengruppen"
            : FilteredGroups.Count == Groups.Count
                ? $"{Groups.Count:N0} Gruppe(n)"
                : $"{FilteredGroups.Count:N0} von {Groups.Count:N0} Gruppe(n)";

    public int VerySafeGroupCount => Groups.Count(group => group.ConfidenceLevel == DuplicateFinderConfidenceLevel.VerySafe);

    public int LikelyGroupCount => Groups.Count(group => group.ConfidenceLevel == DuplicateFinderConfidenceLevel.Likely);

    public int UnclearGroupCount => Groups.Count(group => group.ConfidenceLevel == DuplicateFinderConfidenceLevel.Unclear);

    public string ConfidenceSummary =>
        $"{VerySafeGroupCount:N0} sehr sicher · {LikelyGroupCount:N0} wahrscheinlich · {UnclearGroupCount:N0} unklar";

    public int SafeFilteredGroupCount => FilteredGroups.Count(group => group.IsVerySafe);

    public int BatchReadyGroupCount => FilteredGroups.Count(group => group.IsVerySafe && group.PreflightIsReady);

    public int BatchReadyRemovalCount => FilteredGroups
        .Where(group => group.IsVerySafe && group.PreflightIsReady)
        .Sum(group => group.PlannedRemovalCount);

    public long BatchReadyBytes => FilteredGroups
        .Where(group => group.IsVerySafe && group.PreflightIsReady)
        .Sum(group => group.PlannedReclaimableBytes);

    public string BatchReadyBytesText => MediaIndexBrowseItemViewModel.FormatBytes(BatchReadyBytes);

    public string BatchSummary => SafeFilteredGroupCount == 0
        ? "In der aktuellen Filterung gibt es keine als sehr sicher eingestuften Gruppen."
        : BatchReadyGroupCount == 0
            ? $"{SafeFilteredGroupCount:N0} sehr sichere Gruppe(n) im Filter · noch kein gemeinsamer Preflight bestanden"
            : $"{BatchReadyGroupCount:N0} von {SafeFilteredGroupCount:N0} sehr sicheren Gruppen bereit · {BatchReadyRemovalCount:N0} Datei(en) · {BatchReadyBytesText}";

    public bool HasBatchReadyGroups => BatchReadyGroupCount > 0;

    public bool IsPlanInteractionEnabled => !IsBusy && !IsPreflightBusy && !IsExecutionBusy && !IsBatchOperationBusy;

    public bool HasGroups => Groups.Count > 0;

    public bool HasFilteredGroups => FilteredGroups.Count > 0;

    public bool HasNoDuplicates => !IsBusy && Groups.Count == 0;

    public bool HasNoMatchingGroups => !IsBusy && Groups.Count > 0 && FilteredGroups.Count == 0;

    public bool HasSelectedGroup => SelectedGroup is not null;

    public bool HasNoSelectedGroup => SelectedGroup is null;

    public bool HasExecutionResult => LastExecutionItems.Count > 0;

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RunPreflightCommand.NotifyCanExecuteChanged();
        ExecuteCleanupCommand.NotifyCanExecuteChanged();
        KeepOldestModifiedCommand.NotifyCanExecuteChanged();
        KeepNewestModifiedCommand.NotifyCanExecuteChanged();
        ResetSelectedPlanCommand.NotifyCanExecuteChanged();
        ApplyPreferredEndpointToFilteredCommand.NotifyCanExecuteChanged();
        NotifyBatchCommandsChanged();
        OnPropertyChanged(nameof(IsPlanInteractionEnabled));
        OnPropertyChanged(nameof(HasNoDuplicates));
        OnPropertyChanged(nameof(HasNoMatchingGroups));
    }

    partial void OnIsPreflightBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RunPreflightCommand.NotifyCanExecuteChanged();
        ExecuteCleanupCommand.NotifyCanExecuteChanged();
        KeepOldestModifiedCommand.NotifyCanExecuteChanged();
        KeepNewestModifiedCommand.NotifyCanExecuteChanged();
        ResetSelectedPlanCommand.NotifyCanExecuteChanged();
        ApplyPreferredEndpointToFilteredCommand.NotifyCanExecuteChanged();
        NotifyBatchCommandsChanged();
        OnPropertyChanged(nameof(IsPlanInteractionEnabled));
    }

    partial void OnIsExecutionBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RunPreflightCommand.NotifyCanExecuteChanged();
        ExecuteCleanupCommand.NotifyCanExecuteChanged();
        KeepOldestModifiedCommand.NotifyCanExecuteChanged();
        KeepNewestModifiedCommand.NotifyCanExecuteChanged();
        ResetSelectedPlanCommand.NotifyCanExecuteChanged();
        ApplyPreferredEndpointToFilteredCommand.NotifyCanExecuteChanged();
        NotifyBatchCommandsChanged();
        OnPropertyChanged(nameof(IsPlanInteractionEnabled));
    }

    partial void OnCleanupConfirmationAcceptedChanged(bool value) =>
        ExecuteCleanupCommand.NotifyCanExecuteChanged();

    partial void OnBatchCleanupConfirmationAcceptedChanged(bool value) =>
        ExecuteSafeBatchCleanupCommand.NotifyCanExecuteChanged();

    partial void OnIsBatchOperationBusyChanged(bool value)
    {
        NotifyBatchCommandsChanged();
        CancelBatchOperationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsPlanInteractionEnabled));
    }

    partial void OnLastExecutionItemsChanged(IReadOnlyList<DuplicateCleanupExecutionItemViewModel> value) =>
        OnPropertyChanged(nameof(HasExecutionResult));

    partial void OnGroupsChanged(IReadOnlyList<DuplicateFinderGroupItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasNoDuplicates));
        OnPropertyChanged(nameof(FilterSummary));
        NotifyConfidenceSummaryChanged();
        NotifyPlanSummaryChanged();
    }

    partial void OnFilteredGroupsChanged(IReadOnlyList<DuplicateFinderGroupItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasFilteredGroups));
        OnPropertyChanged(nameof(HasNoMatchingGroups));
        OnPropertyChanged(nameof(FilterSummary));
        ApplyPreferredEndpointToFilteredCommand.NotifyCanExecuteChanged();
        BatchCleanupConfirmationAccepted = false;
        NotifyBatchSummaryChanged();
        NotifyBatchCommandsChanged();
    }

    partial void OnSelectedGroupChanged(DuplicateFinderGroupItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasNoSelectedGroup));
        CleanupConfirmationAccepted = false;
        RunPreflightCommand.NotifyCanExecuteChanged();
        ExecuteCleanupCommand.NotifyCanExecuteChanged();
        KeepOldestModifiedCommand.NotifyCanExecuteChanged();
        KeepNewestModifiedCommand.NotifyCanExecuteChanged();
        ResetSelectedPlanCommand.NotifyCanExecuteChanged();

        PreflightOperationMessage = value is null
            ? "Wähle eine Dublettengruppe aus."
            : value.HasPreflightResult
                ? value.PreflightSummary
                : value.Keeper is null
                    ? "Diese Gruppe besteht nur aus geschützten Backup-/Archiv-Fundorten und wird nicht generisch bereinigt."
                    : value.PlannedRemovalCount == 0
                        ? "Für diese Gruppe ist aktuell keine Datei zur Entfernung vorgesehen."
                        : "Plan prüfen, Referenz bei Bedarf wechseln und anschließend den Preflight starten.";

        ExecutionOperationMessage = value?.PreflightIsReady == true
            ? "Preflight bestanden. Prüfe den Plan noch einmal, bestätige die Papierkorb-Aktion und führe sie dann aus."
            : "Für die Ausführung ist zuerst ein bestandener Preflight erforderlich.";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedMediaTypeChanged(string value) => ApplyFilter();

    partial void OnSelectedConfidenceChanged(string value) => ApplyFilter();

    partial void OnSelectedSortChanged(string value) => ApplyFilter();

    partial void OnSelectedPreferredEndpointChanged(DuplicateFinderEndpointOptionViewModel? value) =>
        ApplyPreferredEndpointToFilteredCommand.NotifyCanExecuteChanged();

    partial void OnDuplicateGroupCountChanged(int value) => OnPropertyChanged(nameof(DuplicateSummary));

    partial void OnRedundantCopyCountChanged(int value) => OnPropertyChanged(nameof(DuplicateSummary));

    partial void OnReclaimableBytesChanged(long value) => OnPropertyChanged(nameof(ReclaimableText));

    private bool CanRefresh() => !IsBusy && !IsPreflightBusy && !IsExecutionBusy && !IsBatchOperationBusy;

    private bool CanRunPreflight() =>
        !IsBusy
        && !IsPreflightBusy
        && !IsExecutionBusy
        && !IsBatchOperationBusy
        && SelectedGroup?.CanRunPreflight == true;

    private bool CanExecuteCleanup() =>
        !IsBusy
        && !IsPreflightBusy
        && !IsExecutionBusy
        && !IsBatchOperationBusy
        && CleanupConfirmationAccepted
        && SelectedGroup?.PreflightIsReady == true;

    private bool CanApplySelectedGroupRule() =>
        !IsBusy
        && !IsPreflightBusy
        && !IsExecutionBusy
        && !IsBatchOperationBusy
        && SelectedGroup?.Keeper is not null;

    private bool CanApplyPreferredEndpointRule() =>
        !IsBusy
        && !IsPreflightBusy
        && !IsExecutionBusy
        && !IsBatchOperationBusy
        && SelectedPreferredEndpoint is not null
        && FilteredGroups.Count > 0;

    private bool CanApplyVerySafeSuggestions() =>
        !IsBusy
        && !IsPreflightBusy
        && !IsExecutionBusy
        && !IsBatchOperationBusy
        && SafeFilteredGroupCount > 0;

    private bool CanRunSafeBatchPreflight() =>
        !IsBusy
        && !IsPreflightBusy
        && !IsExecutionBusy
        && !IsBatchOperationBusy
        && SafeFilteredGroupCount > 0;

    private bool CanExecuteSafeBatchCleanup() =>
        !IsBusy
        && !IsPreflightBusy
        && !IsExecutionBusy
        && !IsBatchOperationBusy
        && BatchCleanupConfirmationAccepted
        && BatchReadyGroupCount > 0;

    private bool CanCancelBatchOperation() => IsBatchOperationBusy && _batchCancellationSource is not null;

    private async Task RefreshAsync()
    {
        if (!CanRefresh())
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Aktuelle Fundorte werden anhand der vorhandenen SHA-256-Identitäten verglichen …";

        try
        {
            var loadResult = await Task.Run(async () =>
            {
                var settings = await _storageSettingsStore.LoadAsync().ConfigureAwait(false);
                var endpointNames = settings.Endpoints
                    .GroupBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().Name,
                        StringComparer.OrdinalIgnoreCase);

                var endpointRoots = BuildEndpointRoots(settings);
                var report = await _duplicateFinderService.FindExactDuplicatesAsync().ConfigureAwait(false);
                var groupViewModels = report.Groups
                    .Select(group => new DuplicateFinderGroupItemViewModel(
                        group,
                        endpointNames,
                        endpointRoots,
                        OpenContainingFolder))
                    .ToArray();

                var endpointOptions = settings.Endpoints
                    .Where(endpoint => endpoint.Enabled)
                    .Where(endpoint => endpoint.Kind is not StorageEndpointKind.Backup and not StorageEndpointKind.Archive)
                    .OrderBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(endpoint => new DuplicateFinderEndpointOptionViewModel(endpoint.Id, endpoint.Name))
                    .ToArray();

                return (Report: report, Groups: groupViewModels, EndpointOptions: endpointOptions);
            });

            var report = loadResult.Report;
            PreferredEndpointOptions = loadResult.EndpointOptions;
            if (SelectedPreferredEndpoint is null
                || !PreferredEndpointOptions.Any(option => string.Equals(
                    option.Id,
                    SelectedPreferredEndpoint.Id,
                    StringComparison.OrdinalIgnoreCase)))
            {
                SelectedPreferredEndpoint = PreferredEndpointOptions.FirstOrDefault();
            }

            PresentLocationCount = report.PresentLocationCount;
            DuplicateGroupCount = report.DuplicateGroupCount;
            DuplicateLocationCount = report.DuplicateLocationCount;
            RedundantCopyCount = report.RedundantCopyCount;
            ReclaimableBytes = report.ReclaimableBytes;

            ReplaceGroups(loadResult.Groups);

            ApplyFilter();

            var duration = report.Duration;
            LastScanMessage = $"Geprüft: {report.CompletedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss} · Dauer {duration:mm\\:ss}";
            IndexLastUpdatedMessage = report.IndexLastUpdatedUtc is { } indexLastUpdated
                ? $"Indexstand: {indexLastUpdated.ToLocalTime():dd.MM.yyyy HH:mm:ss}"
                : "Index wurde noch nicht aufgebaut.";

            StatusMessage = report.IndexLastUpdatedUtc is null
                ? "Der Medienindex wurde noch nicht aufgebaut. Aktualisiere zuerst den Index, damit Dubletten zuverlässig erkannt werden können."
                : report.DuplicateGroupCount == 0
                    ? $"{report.PresentLocationCount:N0} aktuelle Fundorte geprüft. Keine bitidentischen Dubletten im aktuellen Index gefunden."
                    : $"{report.DuplicateGroupCount:N0} Dublettengruppe(n) gefunden. Exakte Dubletten können nach Preflight und ausdrücklicher Bestätigung sicher in den Systempapierkorb verschoben werden.";

            PreflightOperationMessage = report.DuplicateGroupCount == 0
                ? "Kein Bereinigungsplan erforderlich."
                : "Jede Gruppe erhält zusätzlich eine konservative Sicherheitsbewertung. Nur ‚sehr sichere‘ Fälle dürfen gesammelt geplant werden; wahrscheinliche und unklare Fälle bleiben in der Einzelprüfung.";

            BatchOperationMessage = report.DuplicateGroupCount == 0
                ? "Keine Stapelbereinigung erforderlich."
                : $"Automatische Einordnung: {ConfidenceSummary}. Für den Stapel werden ausschließlich ‚sehr sichere‘ Gruppen angeboten.";
            BatchCleanupConfirmationAccepted = false;
            NotifyBatchSummaryChanged();

            if (report.DuplicateGroupCount == 0)
            {
                ExecutionOperationMessage = "Keine Papierkorb-Aktion erforderlich.";
            }
        }
        catch (Exception exception)
        {
            ReplaceGroups(Array.Empty<DuplicateFinderGroupItemViewModel>());
            FilteredGroups = Array.Empty<DuplicateFinderGroupItemViewModel>();
            PreferredEndpointOptions = Array.Empty<DuplicateFinderEndpointOptionViewModel>();
            SelectedPreferredEndpoint = null;
            SelectedGroup = null;
            PresentLocationCount = 0;
            DuplicateGroupCount = 0;
            DuplicateLocationCount = 0;
            RedundantCopyCount = 0;
            ReclaimableBytes = 0;
            LastScanMessage = "Dublettenprüfung fehlgeschlagen.";
            IndexLastUpdatedMessage = "Indexstand konnte nicht gelesen werden.";
            StatusMessage = "Dubletten konnten nicht gelesen werden: "
                + exception.GetBaseException().Message;
            PreflightOperationMessage = "Kein Preflight möglich, solange die Dublettengruppe nicht geladen werden kann.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunPreflightAsync()
    {
        var group = SelectedGroup;
        if (group is null || !CanRunPreflight())
        {
            return;
        }

        DuplicateCleanupPlan plan;
        try
        {
            plan = group.CreateCleanupPlan();
        }
        catch (InvalidOperationException exception)
        {
            PreflightOperationMessage = exception.Message;
            return;
        }

        var planVersion = group.PlanVersion;
        CleanupConfirmationAccepted = false;
        IsPreflightBusy = true;
        group.SetPreflightRunning();
        PreflightOperationMessage = "Preflight läuft: Referenz und Entfernungskandidaten werden direkt auf den Datenträgern verifiziert …";
        ExecutionOperationMessage = "Ausführung gesperrt, solange der aktuelle Preflight läuft.";

        try
        {
            var report = await _duplicateCleanupPlanner.RunPreflightAsync(plan);

            if (group.PlanVersion != planVersion)
            {
                PreflightOperationMessage = "Der Bereinigungsplan wurde während der Prüfung geändert. Das alte Preflight-Ergebnis wurde verworfen.";
                return;
            }

            group.ApplyPreflight(report);
            NotifyBatchSummaryChanged();
            NotifyBatchCommandsChanged();
            PreflightOperationMessage = report.IsReady
                ? $"Preflight bestanden. {report.ReadyRemovalCount:N0} Entfernungskandidat(en) sind für die sichere Ausführung vorbereitet."
                : "Preflight blockiert. Mindestens ein Fundort stimmt nicht mehr sicher mit dem geplanten Zustand überein.";
            ExecutionOperationMessage = report.IsReady
                ? "Prüfe die geplanten Fundorte noch einmal und bestätige anschließend ausdrücklich das Verschieben in den Systempapierkorb."
                : "Ausführung blockiert. Behebe zuerst die gemeldeten Preflight-Probleme.";
            ExecuteCleanupCommand.NotifyCanExecuteChanged();
        }
        catch (Exception exception)
        {
            if (group.PlanVersion == planVersion)
            {
                group.ResetPreflightAfterError(exception.GetBaseException().Message);
            }

            PreflightOperationMessage = "Preflight fehlgeschlagen: " + exception.GetBaseException().Message;
            ExecutionOperationMessage = "Ausführung blockiert, weil der Preflight nicht erfolgreich abgeschlossen wurde.";
        }
        finally
        {
            IsPreflightBusy = false;
        }
    }

    private async Task ExecuteCleanupAsync()
    {
        var group = SelectedGroup;
        if (group is null || !CanExecuteCleanup())
        {
            return;
        }

        DuplicateCleanupPlan plan;
        try
        {
            plan = group.CreateCleanupPlan();
        }
        catch (InvalidOperationException exception)
        {
            ExecutionOperationMessage = exception.Message;
            CleanupConfirmationAccepted = false;
            return;
        }

        IsExecutionBusy = true;
        ExecutionOperationMessage = "Sicherheitsprüfung wird unmittelbar wiederholt; freigegebene Kandidaten werden anschließend in den Systempapierkorb verschoben …";

        var refreshAfterExecution = false;
        string? finalMessage = null;

        try
        {
            var report = await _duplicateCleanupExecutor.ExecuteAsync(plan);

            LastExecutionItems = report.Items
                .Select(item => new DuplicateCleanupExecutionItemViewModel(item))
                .ToArray();

            var movedBytes = MediaIndexBrowseItemViewModel.FormatBytes(report.MovedBytes);
            LastExecutionSummary = report.SucceededCount == 0
                ? $"Keine Datei verschoben · {report.FailedCount:N0} blockiert/fehlgeschlagen"
                : report.FailedCount == 0
                    ? $"{report.SucceededCount:N0} Datei(en) im Papierkorb · {movedBytes} verschoben"
                    : $"{report.SucceededCount:N0} im Papierkorb · {report.FailedCount:N0} blockiert/fehlgeschlagen · {movedBytes} verschoben";

            if (report.IsSuccessful)
            {
                finalMessage = $"Bereinigung abgeschlossen. {report.SucceededCount:N0} Datei(en) wurden in den Systempapierkorb verschoben; der Medienindex wurde gezielt aktualisiert und die Aktion protokolliert. Speicherplatz wird erst beim Leeren des Papierkorbs frei.";
            }
            else if (report.SucceededCount > 0)
            {
                finalMessage = "Bereinigung teilweise abgeschlossen. Bereits verschobene Dateien bleiben im Papierkorb; prüfe die Ergebnisdetails und gegebenenfalls den Medienindex.";
            }
            else
            {
                finalMessage = "Bereinigung nicht ausgeführt. Keine Datei wurde in den Papierkorb verschoben.";
            }

            if (report.GlobalIssues.Count > 0)
            {
                finalMessage += " Hinweis: " + report.GlobalIssues[0];
            }

            group.InvalidatePreflightAfterExecution(
                "Der vorherige Preflight ist nach dem Ausführungsversuch nicht mehr gültig. Vor einer weiteren Aktion muss die Gruppe neu geprüft werden.");
            NotifyBatchSummaryChanged();
            NotifyBatchCommandsChanged();
            ExecuteCleanupCommand.NotifyCanExecuteChanged();
            refreshAfterExecution = report.IndexUpdated && report.SucceededCount > 0;
        }
        catch (Exception exception)
        {
            group.InvalidatePreflightAfterExecution(
                "Der Ausführungszustand ist nach dem Fehler nicht mehr sicher bestätigt. Bitte Preflight erneut ausführen.");
            ExecuteCleanupCommand.NotifyCanExecuteChanged();
            finalMessage = "Bereinigung fehlgeschlagen: " + exception.GetBaseException().Message;
        }
        finally
        {
            CleanupConfirmationAccepted = false;
            IsExecutionBusy = false;
        }

        if (refreshAfterExecution)
        {
            await RefreshAsync();
        }

        if (!string.IsNullOrWhiteSpace(finalMessage))
        {
            ExecutionOperationMessage = finalMessage;
        }
    }

    private void KeepOldestModified()
    {
        if (SelectedGroup?.TryKeepOldestModified() == true)
        {
            PreflightOperationMessage = "Referenzregel angewendet: Die Datei mit der ältesten indexierten Änderungszeit bleibt erhalten. Neuer Preflight erforderlich.";
        }
    }

    private void KeepNewestModified()
    {
        if (SelectedGroup?.TryKeepNewestModified() == true)
        {
            PreflightOperationMessage = "Referenzregel angewendet: Die Datei mit der neuesten indexierten Änderungszeit bleibt erhalten. Neuer Preflight erforderlich.";
        }
    }

    private void ResetSelectedPlan()
    {
        if (SelectedGroup?.ResetPlanToDefault() == true)
        {
            PreflightOperationMessage = "Bereinigungsplan auf den sicheren Standard zurückgesetzt. Neuer Preflight erforderlich.";
        }
    }

    private void ApplyPreferredEndpointToFiltered()
    {
        var endpoint = SelectedPreferredEndpoint;
        if (endpoint is null || FilteredGroups.Count == 0)
        {
            return;
        }

        var changed = 0;
        var matched = 0;

        _suppressPlanChangedNotifications = true;
        try
        {
            foreach (var group in FilteredGroups)
            {
                if (!group.Locations.Any(location =>
                        !location.IsProtectedEndpoint
                        && string.Equals(location.EndpointId, endpoint.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                matched++;
                if (group.TryKeepEndpoint(endpoint.Id))
                {
                    changed++;
                }
            }
        }
        finally
        {
            _suppressPlanChangedNotifications = false;
        }

        if (changed > 0)
        {
            NotifyPlanSummaryChanged();
            RunPreflightCommand.NotifyCanExecuteChanged();
            ExecuteCleanupCommand.NotifyCanExecuteChanged();
        }

        CleanupConfirmationAccepted = false;
        BatchCleanupConfirmationAccepted = false;
        NotifyBatchSummaryChanged();
        NotifyBatchCommandsChanged();
        PreflightOperationMessage = matched == 0
            ? $"Keine der aktuell gefilterten Gruppen besitzt einen bereinigbaren Fundort auf ‚{endpoint.Name}‘."
            : $"Speicherort-Regel auf {matched:N0} gefilterte Gruppe(n) angewendet; {changed:N0} Plan/Pläne wurden geändert. Dateien bleiben unverändert, vor jeder Ausführung ist weiterhin ein Preflight nötig.";
    }

    private void ApplyVerySafeSuggestions()
    {
        var candidates = FilteredGroups
            .Where(group => group.IsVerySafe)
            .ToArray();

        if (candidates.Length == 0)
        {
            BatchOperationMessage = "In der aktuellen Filterung gibt es keine sehr sicheren Gruppen.";
            return;
        }

        var changed = 0;
        _suppressPlanChangedNotifications = true;
        try
        {
            foreach (var group in candidates)
            {
                if (group.ApplyVerySafeSuggestion())
                {
                    changed++;
                }
            }
        }
        finally
        {
            _suppressPlanChangedNotifications = false;
        }

        CleanupConfirmationAccepted = false;
        BatchCleanupConfirmationAccepted = false;
        NotifyPlanSummaryChanged();
        NotifyBatchSummaryChanged();
        NotifyBatchCommandsChanged();
        RunPreflightCommand.NotifyCanExecuteChanged();
        ExecuteCleanupCommand.NotifyCanExecuteChanged();

        BatchOperationMessage = $"{candidates.Length:N0} sehr sichere Gruppe(n) wurden nach der erkannten Original/Kopie-Struktur geplant; {changed:N0} Plan/Pläne haben sich geändert. Es wurde noch keine Datei angefasst.";
    }

    private async Task RunSafeBatchPreflightAsync()
    {
        if (!CanRunSafeBatchPreflight())
        {
            return;
        }

        var candidates = FilteredGroups
            .Where(group => group.IsVerySafe)
            .ToArray();

        if (candidates.Length == 0)
        {
            return;
        }

        ApplyVerySafeSuggestions();

        _batchCancellationSource?.Dispose();
        _batchCancellationSource = new CancellationTokenSource();
        var cancellationToken = _batchCancellationSource.Token;
        IsBatchOperationBusy = true;
        IsPreflightBusy = true;
        BatchCleanupConfirmationAccepted = false;

        var ready = 0;
        var blocked = 0;

        try
        {
            for (var index = 0; index < candidates.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var group = candidates[index];
                BatchOperationMessage = $"Stapel-Preflight {index + 1:N0} von {candidates.Length:N0}: {group.Title}";

                if (!group.CanRunPreflight)
                {
                    blocked++;
                    continue;
                }

                DuplicateCleanupPlan plan;
                try
                {
                    plan = group.CreateCleanupPlan();
                }
                catch (InvalidOperationException exception)
                {
                    group.ResetPreflightAfterError(exception.Message);
                    blocked++;
                    continue;
                }

                var planVersion = group.PlanVersion;
                group.SetPreflightRunning();

                try
                {
                    var report = await Task.Run(
                        () => _duplicateCleanupPlanner.RunPreflightAsync(plan, cancellationToken),
                        cancellationToken);

                    if (group.PlanVersion != planVersion)
                    {
                        group.ResetPreflightAfterError("Der Plan wurde während des Stapel-Preflights geändert.");
                        blocked++;
                        continue;
                    }

                    group.ApplyPreflight(report);
                    if (report.IsReady)
                    {
                        ready++;
                    }
                    else
                    {
                        blocked++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    group.ResetPreflightAfterError(exception.GetBaseException().Message);
                    blocked++;
                }
            }

            BatchOperationMessage = $"Stapel-Preflight abgeschlossen: {ready:N0} Gruppe(n) bereit, {blocked:N0} blockiert. Nur die erfolgreich geprüften sehr sicheren Gruppen können gesammelt ausgeführt werden.";
        }
        catch (OperationCanceledException)
        {
            BatchOperationMessage = $"Stapel-Preflight abgebrochen. Bereits geprüfte Gruppen behalten ihr Ergebnis; aktuell sind {ready:N0} Gruppe(n) bereit.";
        }
        finally
        {
            IsPreflightBusy = false;
            IsBatchOperationBusy = false;
            _batchCancellationSource?.Dispose();
            _batchCancellationSource = null;
            NotifyBatchSummaryChanged();
            NotifyBatchCommandsChanged();
        }
    }

    private async Task ExecuteSafeBatchCleanupAsync()
    {
        if (!CanExecuteSafeBatchCleanup())
        {
            return;
        }

        var candidates = FilteredGroups
            .Where(group => group.IsVerySafe && group.PreflightIsReady)
            .ToArray();

        if (candidates.Length == 0)
        {
            BatchCleanupConfirmationAccepted = false;
            return;
        }

        _batchCancellationSource?.Dispose();
        _batchCancellationSource = new CancellationTokenSource();
        var cancellationToken = _batchCancellationSource.Token;
        IsBatchOperationBusy = true;
        IsExecutionBusy = true;

        var resultItems = new List<DuplicateCleanupExecutionItemViewModel>();
        var succeeded = 0;
        var failed = 0;
        var movedBytes = 0L;
        var processedGroups = 0;
        var warnings = 0;
        var refreshAfterExecution = false;

        try
        {
            for (var index = 0; index < candidates.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var group = candidates[index];
                BatchOperationMessage = $"Sichere Stapelbereinigung {index + 1:N0} von {candidates.Length:N0}: {group.Title}";

                DuplicateCleanupPlan plan;
                try
                {
                    plan = group.CreateCleanupPlan();
                }
                catch (InvalidOperationException exception)
                {
                    group.InvalidatePreflightAfterExecution(exception.Message);
                    failed++;
                    processedGroups++;
                    continue;
                }

                try
                {
                    var report = await Task.Run(
                        () => _duplicateCleanupExecutor.ExecuteAsync(plan, cancellationToken),
                        cancellationToken);

                    resultItems.AddRange(report.Items.Select(item => new DuplicateCleanupExecutionItemViewModel(item)));
                    succeeded += report.SucceededCount;
                    failed += report.FailedCount;
                    movedBytes += report.MovedBytes;
                    warnings += report.GlobalIssues.Count;
                    refreshAfterExecution |= report.SucceededCount > 0 && report.IndexUpdated;
                    processedGroups++;

                    group.InvalidatePreflightAfterExecution(
                        "Die Gruppe wurde im Stapel verarbeitet. Vor einer weiteren Aktion ist ein neuer Preflight erforderlich.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    group.InvalidatePreflightAfterExecution(
                        "Die Stapelausführung ist für diese Gruppe fehlgeschlagen: " + exception.GetBaseException().Message);
                    failed += Math.Max(1, group.PlannedRemovalCount);
                    processedGroups++;
                }
            }

            var movedText = MediaIndexBrowseItemViewModel.FormatBytes(movedBytes);
            BatchOperationMessage = $"Stapel abgeschlossen: {processedGroups:N0} Gruppe(n) verarbeitet · {succeeded:N0} Datei(en) im Papierkorb · {failed:N0} blockiert/fehlgeschlagen · {movedText} verschoben"
                + (warnings > 0 ? $" · {warnings:N0} Systemhinweis(e) – Ergebnisdetails/Medienindex prüfen." : ".");
        }
        catch (OperationCanceledException)
        {
            var movedText = MediaIndexBrowseItemViewModel.FormatBytes(movedBytes);
            BatchOperationMessage = $"Stapel abgebrochen: {processedGroups:N0} Gruppe(n) verarbeitet · {succeeded:N0} Datei(en) bereits im Papierkorb · {movedText} verschoben. Nicht verarbeitete Gruppen bleiben unangetastet.";
        }
        finally
        {
            LastExecutionItems = resultItems.ToArray();
            LastExecutionSummary = BatchOperationMessage;
            BatchCleanupConfirmationAccepted = false;
            IsExecutionBusy = false;
            IsBatchOperationBusy = false;
            _batchCancellationSource?.Dispose();
            _batchCancellationSource = null;
            NotifyBatchSummaryChanged();
            NotifyBatchCommandsChanged();
        }

        var batchCompletionMessage = BatchOperationMessage;
        if (refreshAfterExecution)
        {
            await RefreshAsync();
        }

        BatchOperationMessage = batchCompletionMessage;
    }

    private void CancelBatchOperation()
    {
        if (!CanCancelBatchOperation())
        {
            return;
        }

        BatchOperationMessage = "Abbruch angefordert … Bereits ausgeführte Papierkorb-Aktionen bleiben bestehen und werden sauber nachgeführt.";
        _batchCancellationSource?.Cancel();
    }

    private void OpenContainingFolder(DuplicateFinderLocationItemViewModel location)
    {
        if (string.IsNullOrWhiteSpace(location.AbsolutePath))
        {
            StatusMessage = $"Der Speicherort ‚{location.EndpointName}‘ ist aktuell nicht auflösbar; der Dateimanager kann deshalb nicht geöffnet werden.";
            return;
        }

        if (_fileManagerService.TryOpenContainingFolder(location.AbsolutePath, out var errorMessage))
        {
            StatusMessage = $"Dateimanager für ‚{location.EndpointName}‘ geöffnet.";
            return;
        }

        StatusMessage = errorMessage ?? "Der Dateimanager konnte nicht geöffnet werden.";
    }

    private IReadOnlyDictionary<string, string> BuildEndpointRoots(StorageSettings settings)
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in settings.Endpoints.Where(endpoint => endpoint.Enabled))
        {
            var resolution = _storageLocationResolver.Resolve(endpoint.Location);
            if (resolution.IsAvailable && !string.IsNullOrWhiteSpace(resolution.ResolvedPath))
            {
                roots[endpoint.Id] = resolution.ResolvedPath;
            }
        }

        return roots;
    }

    private void ApplyFilter()
    {
        IEnumerable<DuplicateFinderGroupItemViewModel> query = Groups;

        query = SelectedMediaType switch
        {
            "Bilder" => query.Where(group => group.MediaType == "Bild"),
            "Videos" => query.Where(group => group.MediaType == "Video"),
            "Audio" => query.Where(group => group.MediaType == "Audio"),
            "Unbekannt" => query.Where(group => group.MediaType == "Unbekannt"),
            _ => query
        };

        query = SelectedConfidence switch
        {
            "Sehr sicher" => query.Where(group => group.ConfidenceLevel == DuplicateFinderConfidenceLevel.VerySafe),
            "Wahrscheinlich" => query.Where(group => group.ConfidenceLevel == DuplicateFinderConfidenceLevel.Likely),
            "Unklar" => query.Where(group => group.ConfidenceLevel == DuplicateFinderConfidenceLevel.Unclear),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(group => group.MatchesSearch(SearchText));
        }

        query = SelectedSort switch
        {
            "Sicherheit + Speichergewinn" => query
                .OrderBy(group => group.ConfidenceSortRank)
                .ThenByDescending(group => group.ReclaimableBytes)
                .ThenBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase),
            "Dateigröße ↓" => query
                .OrderByDescending(group => group.Length)
                .ThenBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase),
            "Kopienzahl ↓" => query
                .OrderByDescending(group => group.CopyCount)
                .ThenByDescending(group => group.ReclaimableBytes)
                .ThenBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase),
            "Dateityp + Name" => query
                .OrderBy(group => group.MediaType, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase),
            "Dateiname A–Z" => query
                .OrderBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => query
                .OrderByDescending(group => group.ReclaimableBytes)
                .ThenByDescending(group => group.Length)
                .ThenBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase)
        };

        FilteredGroups = query.ToArray();

        if (SelectedGroup is null || !FilteredGroups.Contains(SelectedGroup))
        {
            SelectedGroup = FilteredGroups.FirstOrDefault();
        }
    }

    private void ReplaceGroups(IReadOnlyList<DuplicateFinderGroupItemViewModel> newGroups)
    {
        foreach (var group in Groups)
        {
            group.PlanChanged -= OnGroupPlanChanged;
        }

        Groups = newGroups;

        foreach (var group in Groups)
        {
            group.PlanChanged += OnGroupPlanChanged;
        }

        NotifyPlanSummaryChanged();
    }

    private void OnGroupPlanChanged(object? sender, EventArgs e)
    {
        if (_suppressPlanChangedNotifications)
        {
            return;
        }

        NotifyPlanSummaryChanged();
        NotifyBatchSummaryChanged();
        CleanupConfirmationAccepted = false;
        BatchCleanupConfirmationAccepted = false;
        RunPreflightCommand.NotifyCanExecuteChanged();
        ExecuteCleanupCommand.NotifyCanExecuteChanged();

        if (ReferenceEquals(sender, SelectedGroup))
        {
            PreflightOperationMessage = SelectedGroup?.CanRunPreflight == true
                ? "Plan geändert. Vor einer späteren Ausführung ist ein neuer Preflight erforderlich."
                : "Plan geändert. Aktuell ist keine generische Entfernung für diese Gruppe vorgesehen.";
            ExecutionOperationMessage = "Plan geändert. Die Ausführung bleibt bis zu einem neuen erfolgreichen Preflight gesperrt.";
        }
    }

    private void NotifyPlanSummaryChanged()
    {
        OnPropertyChanged(nameof(PlannedRemovalCount));
        OnPropertyChanged(nameof(PlannedReclaimableBytes));
        OnPropertyChanged(nameof(PlannedReclaimableText));
        OnPropertyChanged(nameof(PlannedSummary));
    }

    private void NotifyConfidenceSummaryChanged()
    {
        OnPropertyChanged(nameof(VerySafeGroupCount));
        OnPropertyChanged(nameof(LikelyGroupCount));
        OnPropertyChanged(nameof(UnclearGroupCount));
        OnPropertyChanged(nameof(ConfidenceSummary));
    }

    private void NotifyBatchSummaryChanged()
    {
        OnPropertyChanged(nameof(SafeFilteredGroupCount));
        OnPropertyChanged(nameof(BatchReadyGroupCount));
        OnPropertyChanged(nameof(BatchReadyRemovalCount));
        OnPropertyChanged(nameof(BatchReadyBytes));
        OnPropertyChanged(nameof(BatchReadyBytesText));
        OnPropertyChanged(nameof(BatchSummary));
        OnPropertyChanged(nameof(HasBatchReadyGroups));
    }

    private void NotifyBatchCommandsChanged()
    {
        ApplyVerySafeSuggestionsCommand.NotifyCanExecuteChanged();
        RunSafeBatchPreflightCommand.NotifyCanExecuteChanged();
        ExecuteSafeBatchCleanupCommand.NotifyCanExecuteChanged();
        CancelBatchOperationCommand.NotifyCanExecuteChanged();
    }
}
