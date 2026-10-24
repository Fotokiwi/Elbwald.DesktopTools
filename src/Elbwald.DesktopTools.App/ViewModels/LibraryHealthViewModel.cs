using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class LibraryHealthViewModel : ObservableObject
{
    private readonly ILibraryHealthService _libraryHealthService;
    private readonly ILibraryHealthSnapshotStore _snapshotStore;
    private readonly IMediaIndexService _mediaIndexService;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageResolver;
    private readonly INavigationService _navigationService;
    private CancellationTokenSource? _scanCancellation;

    [ObservableProperty]
    private IReadOnlyList<LibraryHealthEndpointItemViewModel> endpoints =
        Array.Empty<LibraryHealthEndpointItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<LibraryHealthIssueItemViewModel> issues =
        Array.Empty<LibraryHealthIssueItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<LibraryHealthIssueItemViewModel> filteredIssues =
        Array.Empty<LibraryHealthIssueItemViewModel>();

    [ObservableProperty]
    private string issueSearchText = string.Empty;

    [ObservableProperty]
    private string selectedIssueSeverity = "Alle";

    [ObservableProperty]
    private string selectedIssueArea = "Alle Bereiche";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string progressTitle = "Prüfung wird vorbereitet …";

    [ObservableProperty]
    private string progressCountMessage = string.Empty;

    [ObservableProperty]
    private string progressDetail = string.Empty;

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private bool isProgressIndeterminate = true;

    [ObservableProperty]
    private string statusMessage = "Bibliotheksstatus wird geladen …";

    [ObservableProperty]
    private string lastScanMessage = "Noch kein Scan ausgeführt.";

    [ObservableProperty]
    private bool hasSnapshot;

    [ObservableProperty]
    private bool isSnapshotStale;

    [ObservableProperty]
    private string snapshotFreshnessMessage = "Noch kein gespeicherter Bibliotheksstatus vorhanden.";

    [ObservableProperty]
    private int totalEndpointCount;

    [ObservableProperty]
    private int availableEndpointCount;

    [ObservableProperty]
    private int offlineEndpointCount;

    [ObservableProperty]
    private int warningCount;

    [ObservableProperty]
    private int problemCount;

    [ObservableProperty]
    private int indexedMediaCount;

    [ObservableProperty]
    private int presentIndexedMediaCount;

    [ObservableProperty]
    private int presentIndexedLocationCount;

    [ObservableProperty]
    private int offlineIndexedMediaCount;

    [ObservableProperty]
    private int missingIndexedMediaCount;

    [ObservableProperty]
    private int unconfiguredIndexedMediaCount;

    [ObservableProperty]
    private int staleIndexedLocationCount;

    [ObservableProperty]
    private int missingCaptureDateIndexCount;

    [ObservableProperty]
    private int analyzedImageCount;

    [ObservableProperty]
    private int missingCaptureDateCount;

    [ObservableProperty]
    private int conflictingCaptureDateCount;

    [ObservableProperty]
    private int suspiciousCaptureDateCount;

    [ObservableProperty]
    private int missingDimensionsCount;

    [ObservableProperty]
    private int metadataErrorCount;

    [ObservableProperty]
    private string overallHealthTitle = "Wird geprüft …";

    [ObservableProperty]
    private string overallHealthMessage = "Elbwald bewertet Speicherorte, Medienindex und Metadatenqualität.";

    [ObservableProperty]
    private string overallHealthHint = "Die Bewertung ist read-only und verändert keine Originaldateien.";

    [ObservableProperty]
    private bool hasOverallProblem;

    [ObservableProperty]
    private bool hasOverallWarning;

    [ObservableProperty]
    private bool hasOverallHealthy;

    [ObservableProperty]
    private IReadOnlyList<LibraryHealthRecommendationItemViewModel> recommendations =
        Array.Empty<LibraryHealthRecommendationItemViewModel>();

    [ObservableProperty]
    private string indexLastUpdatedMessage = "Medienindex wird geprüft …";

    public LibraryHealthViewModel(
        ILibraryHealthService libraryHealthService,
        ILibraryHealthSnapshotStore snapshotStore,
        IMediaIndexService mediaIndexService,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageResolver,
        INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(libraryHealthService);
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        ArgumentNullException.ThrowIfNull(storageSettingsStore);
        ArgumentNullException.ThrowIfNull(storageResolver);
        ArgumentNullException.ThrowIfNull(navigationService);

        _libraryHealthService = libraryHealthService;
        _snapshotStore = snapshotStore;
        _mediaIndexService = mediaIndexService;
        _storageSettingsStore = storageSettingsStore;
        _storageResolver = storageResolver;
        _navigationService = navigationService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        UpdateMediaIndexCommand = new AsyncRelayCommand(UpdateMediaIndexAsync, () => !IsBusy);
        CancelScanCommand = new RelayCommand(CancelScan, () => CanCancelScan);
        OpenSettingsCommand = new RelayCommand(() => _navigationService.NavigateTo("settings"));
        OpenDiagnosticsCommand = new RelayCommand(() => _navigationService.NavigateTo("diagnostics"));
        OpenMediaIndexCommand = new RelayCommand(() => _navigationService.NavigateTo("media-index"));
        OpenDuplicateFinderCommand = new RelayCommand(() => _navigationService.NavigateTo("duplicate-finder"));
        OpenMetadataCommand = new RelayCommand(() => _navigationService.NavigateTo("metadata"));

        _ = InitializeAsync();
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand UpdateMediaIndexCommand { get; }

    public IRelayCommand CancelScanCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand OpenDiagnosticsCommand { get; }

    public IRelayCommand OpenMediaIndexCommand { get; }

    public IRelayCommand OpenDuplicateFinderCommand { get; }

    public IRelayCommand OpenMetadataCommand { get; }

    public bool CanCancelScan => IsBusy && _scanCancellation is { IsCancellationRequested: false };

    public bool HasIndexData => IndexedMediaCount > 0;

    public bool HasMetadataQualityData => AnalyzedImageCount > 0;

    public string MetadataQualitySummary =>
        AnalyzedImageCount == 0
            ? "Noch keine erreichbaren Bildmedien geprüft"
            : $"{AnalyzedImageCount:N0} Bildmedien read-only geprüft";

    public string MetadataErrorSummary =>
        MetadataErrorCount == 0
            ? "Keine Metadaten-Lesefehler"
            : $"{MetadataErrorCount:N0} Metadaten-Lesefehler";

    public string IndexAvailabilitySummary =>
        IndexedMediaCount == 0
            ? "Noch keine Medien indexiert"
            : $"{PresentIndexedMediaCount:N0} von {IndexedMediaCount:N0} physisch erreichbar";

    public string MissingMediaSummary =>
        UnconfiguredIndexedMediaCount > 0
            ? $"{MissingIndexedMediaCount:N0} fehlen · {UnconfiguredIndexedMediaCount:N0} ohne konfigurierten Ort"
            : $"{MissingIndexedMediaCount:N0} tatsächlich nicht erreichbar";

    public string IndexDetailSummary =>
        $"{StaleIndexedLocationCount:N0} veraltete Indexorte · {MissingCaptureDateIndexCount:N0} Bildmedien ohne Aufnahmedatum";

    public bool HasEndpoints => Endpoints.Count > 0;

    public IReadOnlyList<string> IssueSeverityFilters { get; } =
        ["Alle", "Probleme", "Warnungen", "Infos"];

    public IReadOnlyList<string> IssueAreaFilters { get; } =
        ["Alle Bereiche", "Speicherorte", "Medienindex", "Metadaten", "Allgemein"];

    public bool HasIssues => Issues.Count > 0;

    public bool HasFilteredIssues => FilteredIssues.Count > 0;

    public bool HasNoMatchingIssues => !IsBusy && HasIssues && FilteredIssues.Count == 0;

    public string IssueFilterSummary =>
        !HasIssues
            ? "Keine Auffälligkeiten"
            : FilteredIssues.Count == Issues.Count
                ? $"{Issues.Count:N0} Auffälligkeit(en)"
                : $"{FilteredIssues.Count:N0} von {Issues.Count:N0} Auffälligkeit(en)";

    public string DuplicateFinderReadinessSummary =>
        PresentIndexedLocationCount >= 2
            ? $"{PresentIndexedLocationCount:N0} aktuelle Fundorte stehen für die bitidentische Dublettenprüfung bereit."
            : "Für die Dublettenprüfung werden mindestens zwei aktuelle Index-Fundorte benötigt.";

    public bool HasRecommendations => Recommendations.Count > 0;

    public bool HasNoRecommendations => Recommendations.Count == 0;

    public bool HasNoIssues => !IsBusy && Issues.Count == 0;

    public bool HasNoEndpoints => !IsBusy && Endpoints.Count == 0;

    public string EndpointSummary =>
        TotalEndpointCount == 1
            ? "1 aktiver Endpunkt"
            : $"{TotalEndpointCount:N0} aktive Endpunkte";

    public string AvailabilitySummary =>
        OfflineEndpointCount > 0
            ? $"{AvailableEndpointCount:N0} online · {OfflineEndpointCount:N0} offline"
            : AvailableEndpointCount == 1
                ? "1 online"
                : $"{AvailableEndpointCount:N0} online";

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        UpdateMediaIndexCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCancelScan));
        OnPropertyChanged(nameof(HasNoIssues));
        OnPropertyChanged(nameof(HasNoMatchingIssues));
        OnPropertyChanged(nameof(HasNoEndpoints));
    }

    partial void OnEndpointsChanged(IReadOnlyList<LibraryHealthEndpointItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasEndpoints));
        OnPropertyChanged(nameof(HasNoEndpoints));
    }

    partial void OnIssuesChanged(IReadOnlyList<LibraryHealthIssueItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(HasNoIssues));
        ApplyIssueFilter();
    }

    partial void OnFilteredIssuesChanged(IReadOnlyList<LibraryHealthIssueItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasFilteredIssues));
        OnPropertyChanged(nameof(HasNoMatchingIssues));
        OnPropertyChanged(nameof(IssueFilterSummary));
    }

    partial void OnIssueSearchTextChanged(string value) => ApplyIssueFilter();

    partial void OnSelectedIssueSeverityChanged(string value) => ApplyIssueFilter();

    partial void OnSelectedIssueAreaChanged(string value) => ApplyIssueFilter();

    partial void OnRecommendationsChanged(IReadOnlyList<LibraryHealthRecommendationItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(HasNoRecommendations));
    }

    partial void OnTotalEndpointCountChanged(int value)
    {
        OnPropertyChanged(nameof(EndpointSummary));
    }

    partial void OnAvailableEndpointCountChanged(int value)
    {
        OnPropertyChanged(nameof(AvailabilitySummary));
    }

    partial void OnOfflineEndpointCountChanged(int value)
    {
        OnPropertyChanged(nameof(AvailabilitySummary));
    }

    partial void OnIndexedMediaCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasIndexData));
        OnPropertyChanged(nameof(IndexAvailabilitySummary));
    }

    partial void OnPresentIndexedMediaCountChanged(int value)
    {
        OnPropertyChanged(nameof(IndexAvailabilitySummary));
    }

    partial void OnPresentIndexedLocationCountChanged(int value)
    {
        OnPropertyChanged(nameof(DuplicateFinderReadinessSummary));
    }

    partial void OnMissingIndexedMediaCountChanged(int value)
    {
        OnPropertyChanged(nameof(MissingMediaSummary));
    }

    partial void OnUnconfiguredIndexedMediaCountChanged(int value)
    {
        OnPropertyChanged(nameof(MissingMediaSummary));
    }

    partial void OnStaleIndexedLocationCountChanged(int value)
    {
        OnPropertyChanged(nameof(IndexDetailSummary));
    }

    partial void OnMissingCaptureDateIndexCountChanged(int value)
    {
        OnPropertyChanged(nameof(IndexDetailSummary));
    }

    partial void OnAnalyzedImageCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMetadataQualityData));
        OnPropertyChanged(nameof(MetadataQualitySummary));
    }

    partial void OnMetadataErrorCountChanged(int value)
    {
        OnPropertyChanged(nameof(MetadataErrorSummary));
    }

    private void ApplyIssueFilter()
    {
        if (string.IsNullOrWhiteSpace(IssueSearchText)
            && string.Equals(SelectedIssueSeverity, "Alle", StringComparison.Ordinal)
            && string.Equals(SelectedIssueArea, "Alle Bereiche", StringComparison.Ordinal))
        {
            // Reuse the already prepared array instead of copying a potentially large
            // issue collection on every snapshot load.
            FilteredIssues = Issues;
            return;
        }

        IEnumerable<LibraryHealthIssueItemViewModel> query = Issues;

        query = SelectedIssueSeverity switch
        {
            "Probleme" => query.Where(issue => issue.SeverityValue == LibraryHealthSeverity.Problem),
            "Warnungen" => query.Where(issue => issue.SeverityValue == LibraryHealthSeverity.Warning),
            "Infos" => query.Where(issue => issue.SeverityValue == LibraryHealthSeverity.Info),
            _ => query
        };

        if (!string.Equals(SelectedIssueArea, "Alle Bereiche", StringComparison.Ordinal))
        {
            query = query.Where(issue => string.Equals(
                issue.Area,
                SelectedIssueArea,
                StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(IssueSearchText))
        {
            query = query.Where(issue => issue.MatchesSearch(IssueSearchText));
        }

        FilteredIssues = query.ToArray();
        OnPropertyChanged(nameof(HasNoMatchingIssues));
        OnPropertyChanged(nameof(IssueFilterSummary));
    }

    private async Task InitializeAsync()
    {
        // Give Avalonia one dispatcher turn to render the page before any startup work begins.
        // Snapshot JSON, SQLite and endpoint resolution may all complete synchronously in parts
        // even though their public APIs are async.
        await Task.Yield();

        try
        {
            var startup = await LoadSnapshotStateAsync();

            if (startup.Snapshot is null)
            {
                HasSnapshot = false;
                IsSnapshotStale = false;
                SnapshotFreshnessMessage = "Noch kein gespeicherter Bibliotheksstatus vorhanden.";
                StatusMessage = "Noch kein Bibliotheksstatus vorhanden. Die erste vollständige Prüfung wird gestartet …";
                await RefreshAsync();
                return;
            }

            ApplyReport(
                startup.Snapshot.Report,
                startup.MediaIndexSummary,
                startup.Projection!);
            ApplySnapshotFreshness(
                startup.Freshness ?? LibraryHealthSnapshotFreshness.Current,
                startup.AvailabilityChanged);

            StatusMessage = IsSnapshotStale
                ? "Gespeicherter Bibliotheksstatus geladen. Der Stand ist veraltet; eine neue Prüfung wird empfohlen. Es läuft kein automatischer Vollscan."
                : "Gespeicherter Bibliotheksstatus geladen. Keine erneute Vollprüfung erforderlich.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Der gespeicherte Bibliotheksstatus konnte nicht geladen werden: "
                            + exception.GetBaseException().Message;
        }
    }

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        IsBusy = true;
        OnPropertyChanged(nameof(CanCancelScan));
        CancelScanCommand.NotifyCanExecuteChanged();
        BeginProgress("Bibliotheksstatus wird vorbereitet …");

        try
        {
            await RunFullScanAsync(
                "Bibliotheksstatus wird aktualisiert …",
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = HasSnapshot
                ? "Bibliotheksprüfung abgebrochen. Der zuletzt gespeicherte Status bleibt erhalten; es wurden keine Dateien verändert."
                : "Bibliotheksprüfung abgebrochen. Es wurden keine Dateien verändert.";
        }
        finally
        {
            _scanCancellation = null;
            IsBusy = false;
        }
    }

    private async Task UpdateMediaIndexAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BeginProgress("Medienindex wird aktualisiert …");
        StatusMessage = "Medienindex wird aktualisiert. Offline-Speicherorte werden dabei übersprungen …";

        try
        {
            var settings = await _storageSettingsStore.LoadAsync();
            var result = await Task.Run(() => _mediaIndexService.IndexAsync(settings));

            var updateMessage = result.Issues.Count == 0
                ? $"Medienindex aktualisiert · {result.FilesIndexed:N0} Datei(en) geprüft."
                : $"Medienindex aktualisiert · {result.FilesIndexed:N0} Datei(en) geprüft · {result.Issues.Count:N0} Hinweis(e).";

            await LoadSnapshotAsync(
                updateMessage + " Der Bibliotheksstatus wird nicht automatisch neu vollständig geprüft.");
        }
        catch (Exception exception)
        {
            StatusMessage = "Der Medienindex konnte nicht aktualisiert werden: " + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSnapshotAsync(string statusPrefix)
    {
        var startup = await LoadSnapshotStateAsync();

        if (startup.Snapshot is null)
        {
            HasSnapshot = false;
            IsSnapshotStale = false;
            SnapshotFreshnessMessage = "Noch kein gespeicherter Bibliotheksstatus vorhanden.";
            StatusMessage = statusPrefix + " Für einen Bibliotheksstatus ist einmalig ‚Neu prüfen‘ erforderlich.";
            return;
        }

        ApplyReport(
            startup.Snapshot.Report,
            startup.MediaIndexSummary,
            startup.Projection!);
        ApplySnapshotFreshness(
            startup.Freshness ?? LibraryHealthSnapshotFreshness.Current,
            startup.AvailabilityChanged);

        StatusMessage = IsSnapshotStale
            ? statusPrefix + " Der angezeigte Health-Snapshot ist dadurch veraltet; ‚Neu prüfen‘ aktualisiert ihn."
            : statusPrefix;
    }

    private async Task RunFullScanAsync(
        string initialStatus,
        CancellationToken cancellationToken = default)
    {
        StatusMessage = initialStatus;

        try
        {
            var settings = await _storageSettingsStore.LoadAsync(cancellationToken);

            // The complete health scan deliberately runs away from Avalonia's UI context.
            // Several health phases contain long synchronous loops between awaits; keeping the
            // outer operation on a worker prevents those continuations from freezing the window.
            var progress = new Progress<LibraryHealthProgress>(UpdateProgress);
            var report = await Task.Run(
                () => _libraryHealthService.ScanAsync(
                    settings,
                    cancellationToken,
                    progress),
                cancellationToken);

            var completion = await BuildCompletedScanStateAsync(
                report,
                settings,
                cancellationToken);

            ApplyReport(report, completion.MediaIndexSummary, completion.Projection);
            var snapshotWarning = completion.SnapshotWarning;
            HasSnapshot = snapshotWarning is null;
            IsSnapshotStale = false;
            SnapshotFreshnessMessage = snapshotWarning is null
                ? "Stand ist aktuell – seit dieser Prüfung wurden Speicherorte und Medienindex nicht verändert."
                : "Die Prüfung ist aktuell, konnte aber nicht dauerhaft als Snapshot gespeichert werden.";

            StatusMessage = BuildCompletedScanStatus(report) + snapshotWarning;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (!HasSnapshot)
            {
                ClearReportState();
                OverallHealthTitle = "Prüfung fehlgeschlagen";
                OverallHealthMessage = "Der Bibliotheksstatus konnte nicht vollständig ermittelt werden.";
                OverallHealthHint = "Öffne das Diagnoseprotokoll und wiederhole die Prüfung.";
                HasOverallProblem = true;
                HasOverallWarning = false;
                HasOverallHealthy = false;
                LastScanMessage = "Der letzte Scan wurde nicht erfolgreich abgeschlossen.";
            }

            StatusMessage = "Der Bibliotheksstatus konnte nicht geladen werden: " + exception.GetBaseException().Message;
        }
    }

    private Task<LibraryHealthSnapshotLoadState> LoadSnapshotStateAsync(
        CancellationToken cancellationToken = default)
    {
        // Keep the complete startup chain away from Avalonia's synchronization context.
        // Microsoft.Data.Sqlite and JSON deserialization both contain synchronous CPU work
        // between awaits, which is noticeable with a large real-world library.
        return Task.Run(
            async () =>
            {
                var settings = await _storageSettingsStore
                    .LoadAsync(cancellationToken)
                    .ConfigureAwait(false);
                var mediaIndexSummary = await _mediaIndexService
                    .GetSummaryAsync(cancellationToken)
                    .ConfigureAwait(false);
                var snapshot = await _snapshotStore
                    .LoadAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (snapshot is null)
                {
                    return new LibraryHealthSnapshotLoadState(
                        settings,
                        mediaIndexSummary,
                        null,
                        null,
                        AvailabilityChanged: false,
                        Projection: null);
                }

                var availabilityChanged = HasStorageAvailabilityChanged(
                    snapshot.Report,
                    settings);
                var freshness = _snapshotStore.EvaluateFreshness(
                    snapshot,
                    settings,
                    mediaIndexSummary);
                var projection = CreateReportProjection(snapshot.Report);

                return new LibraryHealthSnapshotLoadState(
                    settings,
                    mediaIndexSummary,
                    snapshot,
                    freshness,
                    availabilityChanged,
                    projection);
            },
            cancellationToken);
    }

    private Task<LibraryHealthCompletedScanState> BuildCompletedScanStateAsync(
        LibraryHealthReport report,
        StorageSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            async () =>
            {
                var mediaIndexSummary = await _mediaIndexService
                    .GetSummaryAsync(cancellationToken)
                    .ConfigureAwait(false);

                string? snapshotWarning = null;
                try
                {
                    await _snapshotStore
                        .SaveAsync(
                            report,
                            settings,
                            mediaIndexSummary,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    snapshotWarning = " Der aktuelle Report konnte nicht als Snapshot gespeichert werden: "
                                      + exception.GetBaseException().Message;
                }

                return new LibraryHealthCompletedScanState(
                    mediaIndexSummary,
                    CreateReportProjection(report),
                    snapshotWarning);
            },
            cancellationToken);
    }

    private static LibraryHealthReportProjection CreateReportProjection(
        LibraryHealthReport report)
    {
        var endpointIssues = report.Issues
            .GroupBy(issue => issue.EndpointId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new IssueCounts(
                    group.Count(issue => issue.Severity == LibraryHealthSeverity.Warning),
                    group.Count(issue => issue.Severity == LibraryHealthSeverity.Problem)),
                StringComparer.OrdinalIgnoreCase);

        var endpoints = report.Endpoints
            .Select(result =>
            {
                endpointIssues.TryGetValue(result.Endpoint.Id, out var counts);
                return new LibraryHealthEndpointItemViewModel(
                    result,
                    counts?.Warnings ?? 0,
                    counts?.Problems ?? 0);
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var endpointNames = report.Endpoints
            .Select(result => result.Endpoint)
            .GroupBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Name,
                StringComparer.OrdinalIgnoreCase);

        var issues = report.Issues
            .Select(issue => new LibraryHealthIssueItemViewModel(
                endpointNames.TryGetValue(issue.EndpointId, out var endpointName)
                    ? endpointName
                    : string.Equals(issue.EndpointId, "media-index", StringComparison.OrdinalIgnoreCase)
                        ? "Medienindex"
                        : issue.EndpointId,
                issue))
            .OrderByDescending(issue => issue.SeverityValue == LibraryHealthSeverity.Problem)
            .ThenByDescending(issue => issue.SeverityValue == LibraryHealthSeverity.Warning)
            .ThenBy(issue => issue.EndpointName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new LibraryHealthReportProjection(endpoints, issues);
    }

    private void ApplyReport(
        LibraryHealthReport report,
        MediaIndexSummary mediaIndexSummary,
        LibraryHealthReportProjection projection)
    {
        Endpoints = projection.Endpoints;
        Issues = projection.Issues;

        IndexedMediaCount = report.IndexSummary.IndexedMediaCount;
        PresentIndexedMediaCount = report.IndexSummary.PresentMediaCount;
        PresentIndexedLocationCount = mediaIndexSummary.PresentLocationCount;
        OfflineIndexedMediaCount = report.IndexSummary.OfflineMediaCount;
        MissingIndexedMediaCount = report.IndexSummary.MissingMediaCount;
        UnconfiguredIndexedMediaCount = report.IndexSummary.UnconfiguredMediaCount;
        StaleIndexedLocationCount = report.IndexSummary.StaleLocationCount;
        MissingCaptureDateIndexCount = report.IndexSummary.MissingCaptureDateCount;

        AnalyzedImageCount = report.Endpoints.Sum(endpoint => endpoint.ImageCount);
        MissingCaptureDateCount = report.Endpoints.Sum(endpoint => endpoint.MissingCaptureDateCount);
        ConflictingCaptureDateCount = report.Endpoints.Sum(endpoint => endpoint.ConflictingCaptureDateCount);
        SuspiciousCaptureDateCount = report.Endpoints.Sum(endpoint => endpoint.SuspiciousCaptureDateCount);
        MissingDimensionsCount = report.Endpoints.Sum(endpoint => endpoint.MissingDimensionsCount);
        MetadataErrorCount = report.Endpoints.Sum(endpoint => endpoint.MetadataErrorCount);

        IndexLastUpdatedMessage = mediaIndexSummary.LastIndexedUtc is { } lastIndexed
            ? $"Index zuletzt aktualisiert: {lastIndexed.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Medienindex wurde noch nicht aufgebaut.";

        TotalEndpointCount = report.Endpoints.Count;
        AvailableEndpointCount = report.Endpoints.Count(endpoint => endpoint.StorageStatus == StorageLocationStatus.Available);
        OfflineEndpointCount = report.Endpoints.Count(endpoint => endpoint.StorageStatus == StorageLocationStatus.VolumeUnavailable);
        WarningCount = report.WarningCount;
        ProblemCount = report.ProblemCount;

        UpdateAssessment(report, report.Endpoints.Count);

        var duration = report.CompletedAtUtc - report.StartedAtUtc;
        LastScanMessage = $"Letzte Prüfung: {report.CompletedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm} · Dauer {duration:mm\\:ss}";
        HasSnapshot = true;
    }

    private void ApplySnapshotFreshness(
        LibraryHealthSnapshotFreshness freshness,
        bool availabilityChanged = false)
    {
        HasSnapshot = true;
        IsSnapshotStale = freshness.IsStale || availabilityChanged;

        var changedAreas = new List<string>(3);
        if (freshness.StorageSettingsChanged)
        {
            changedAreas.Add("Speicherort-Konfiguration");
        }

        if (freshness.MediaIndexChanged)
        {
            changedAreas.Add("Medienindex");
        }

        if (availabilityChanged)
        {
            changedAreas.Add("Erreichbarkeit der Speicherorte");
        }

        SnapshotFreshnessMessage = changedAreas.Count == 0
            ? "Stand ist aktuell – seit dieser Prüfung wurden Speicherorte und Medienindex nicht verändert."
            : "Seit der letzten Health-Prüfung geändert: " + string.Join(" · ", changedAreas) + ".";
    }

    private bool HasStorageAvailabilityChanged(
        LibraryHealthReport report,
        StorageSettings settings)
    {
        var snapshotByEndpoint = report.Endpoints.ToDictionary(
            result => result.Endpoint.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in settings.Endpoints.Where(endpoint => endpoint.Enabled))
        {
            if (!snapshotByEndpoint.TryGetValue(endpoint.Id, out var snapshotResult))
            {
                return true;
            }

            try
            {
                var currentStatus = _storageResolver.Resolve(endpoint.Location).Status;
                if (currentStatus != snapshotResult.StorageStatus)
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private string BuildCompletedScanStatus(LibraryHealthReport report)
    {
        if (report.Endpoints.Count == 0)
        {
            return report.Issues.Count == 0
                ? "Medienindex geprüft. Für einen Dateisystem-Scan ist noch kein aktiver Speicherort konfiguriert."
                : $"Medienindex geprüft · {report.ProblemCount:N0} Problem(e) · {report.WarningCount:N0} Warnung(en). Für den Dateisystem-Scan fehlt ein aktiver Speicherort.";
        }

        if (report.ProblemCount == 0 && report.WarningCount == 0)
        {
            return OfflineEndpointCount > 0
                ? $"Bibliothek geprüft. {OfflineEndpointCount:N0} Speicherort(e) sind derzeit offline; deren Medien werden nicht als verloren gewertet."
                : "Keine Auffälligkeiten gefunden. Speicherorte und Medienindex wurden read-only geprüft.";
        }

        return $"{report.Endpoints.Count:N0} Endpunkt(e) + Medienindex geprüft · {report.ProblemCount:N0} Problem(e) · {report.WarningCount:N0} Warnung(en).";
    }

    private void ClearReportState()
    {
        Endpoints = Array.Empty<LibraryHealthEndpointItemViewModel>();
        Issues = Array.Empty<LibraryHealthIssueItemViewModel>();
        TotalEndpointCount = 0;
        AvailableEndpointCount = 0;
        OfflineEndpointCount = 0;
        WarningCount = 0;
        ProblemCount = 0;
        IndexedMediaCount = 0;
        PresentIndexedMediaCount = 0;
        PresentIndexedLocationCount = 0;
        OfflineIndexedMediaCount = 0;
        MissingIndexedMediaCount = 0;
        UnconfiguredIndexedMediaCount = 0;
        StaleIndexedLocationCount = 0;
        MissingCaptureDateIndexCount = 0;
        Recommendations = Array.Empty<LibraryHealthRecommendationItemViewModel>();
        AnalyzedImageCount = 0;
        MissingCaptureDateCount = 0;
        ConflictingCaptureDateCount = 0;
        SuspiciousCaptureDateCount = 0;
        MissingDimensionsCount = 0;
        MetadataErrorCount = 0;
        IndexLastUpdatedMessage = "Medienindex konnte nicht geprüft werden.";
    }

    private void BeginProgress(string title)
    {
        ProgressTitle = title;
        ProgressCountMessage = string.Empty;
        ProgressDetail = string.Empty;
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
    }

    private void CancelScan()
    {
        if (_scanCancellation is null || _scanCancellation.IsCancellationRequested)
        {
            return;
        }

        StatusMessage = "Bibliotheksprüfung wird abgebrochen …";
        ProgressTitle = "Prüfung wird abgebrochen …";
        ProgressDetail = "Der aktuelle Lesevorgang wird noch sauber beendet.";
        IsProgressIndeterminate = true;
        _scanCancellation.Cancel();
        OnPropertyChanged(nameof(CanCancelScan));
        CancelScanCommand.NotifyCanExecuteChanged();
    }

    private void UpdateProgress(LibraryHealthProgress progress)
    {
        var endpointLabel = progress.EndpointTotal > 0
            ? $"Speicherort {progress.EndpointIndex:N0} von {progress.EndpointTotal:N0}"
            : string.Empty;

        switch (progress.Stage)
        {
            case LibraryHealthScanStage.ResolvingStorageLocations:
                ProgressTitle = "Speicherorte werden aufgelöst …";
                ProgressCountMessage = string.Empty;
                ProgressDetail = "Volume-IDs und aktuelle Verfügbarkeit werden geprüft.";
                ProgressPercent = 0;
                IsProgressIndeterminate = true;
                break;

            case LibraryHealthScanStage.ScanningFiles:
                ProgressTitle = string.IsNullOrWhiteSpace(progress.EndpointName)
                    ? "Dateisystem wird geprüft …"
                    : $"{endpointLabel} · {progress.EndpointName}";
                ProgressCountMessage = $"{progress.FilesDiscovered:N0} Dateien entdeckt";
                ProgressDetail = progress.CurrentPath ?? string.Empty;
                ProgressPercent = 0;
                IsProgressIndeterminate = true;
                break;

            case LibraryHealthScanStage.ReadingMetadata:
                ProgressTitle = string.IsNullOrWhiteSpace(progress.EndpointName)
                    ? "Metadaten werden geprüft …"
                    : $"{endpointLabel} · Metadaten · {progress.EndpointName}";
                ProgressCountMessage = progress.Total > 0
                    ? $"{progress.Processed:N0} von {progress.Total:N0} Bilddateien · {progress.FilesDiscovered:N0} Dateien entdeckt"
                    : $"{progress.Processed:N0} Bilddateien geprüft · {progress.FilesDiscovered:N0} Dateien entdeckt";
                ProgressDetail = progress.CurrentPath ?? string.Empty;
                ApplyDeterminateProgress(progress.Processed, progress.Total);
                break;

            case LibraryHealthScanStage.InspectingIndexLocations:
                ProgressTitle = "Medienindex · Fundorte werden geprüft …";
                ProgressCountMessage = progress.Total > 0
                    ? $"{progress.Processed:N0} von {progress.Total:N0} Fundorten"
                    : "Indexdaten werden geladen …";
                ProgressDetail = "Erreichbarkeit und Konsistenz der gespeicherten Fundorte";
                ApplyDeterminateProgress(progress.Processed, progress.Total);
                break;

            case LibraryHealthScanStage.InspectingIndexMetadata:
                ProgressTitle = "Medienindex · Metadatenstatus wird geprüft …";
                ProgressCountMessage = progress.Total > 0
                    ? $"{progress.Processed:N0} von {progress.Total:N0} Medien"
                    : "Indexdaten werden ausgewertet …";
                ProgressDetail = "Aufnahmedatum und Indexqualität";
                ApplyDeterminateProgress(progress.Processed, progress.Total);
                break;

            case LibraryHealthScanStage.Finalizing:
                ProgressTitle = "Ergebnisse werden zusammengeführt …";
                ProgressCountMessage = string.Empty;
                ProgressDetail = "Auffälligkeiten und Empfehlungen werden vorbereitet.";
                ProgressPercent = 0;
                IsProgressIndeterminate = true;
                break;

            case LibraryHealthScanStage.Completed:
                ProgressTitle = "Bibliotheksprüfung abgeschlossen";
                ProgressCountMessage = "100 %";
                ProgressDetail = string.Empty;
                ProgressPercent = 100;
                IsProgressIndeterminate = false;
                break;
        }
    }

    private void ApplyDeterminateProgress(int processed, int total)
    {
        if (total <= 0)
        {
            ProgressPercent = 0;
            IsProgressIndeterminate = true;
            return;
        }

        ProgressPercent = Math.Clamp(processed * 100d / total, 0d, 100d);
        IsProgressIndeterminate = false;
    }

    private void UpdateAssessment(
        LibraryHealthReport report,
        int enabledEndpointCount)
    {
        var recommendationItems = new List<LibraryHealthRecommendationItemViewModel>();
        var issueKinds = report.Issues.Select(issue => issue.Kind).ToHashSet();

        if (enabledEndpointCount == 0)
        {
            recommendationItems.Add(new LibraryHealthRecommendationItemViewModel(
                "Zuerst",
                "Medienordner hinzufügen",
                "Für eine vollständige Gesundheitsprüfung braucht Elbwald mindestens einen aktiven Bibliotheksordner.",
                "Speicherorte öffnen",
                OpenSettingsCommand,
                isWarning: true));
        }

        if (report.IndexSummary.IndexedMediaCount == 0 && enabledEndpointCount > 0)
        {
            recommendationItems.Add(new LibraryHealthRecommendationItemViewModel(
                "Zuerst",
                "Medienindex aufbauen",
                "Die Bibliotheksordner sind konfiguriert, aber der Medienindex enthält noch keine Medien. Baue ihn auf, damit Elbwald Bestand und Pfade zuverlässig vergleichen kann.",
                "Index aktualisieren",
                UpdateMediaIndexCommand,
                isWarning: true));
        }

        if (report.IndexSummary.MissingMediaCount > 0)
        {
            recommendationItems.Add(new LibraryHealthRecommendationItemViewModel(
                "Hohe Priorität",
                $"{report.IndexSummary.MissingMediaCount:N0} fehlende Medien prüfen",
                "Diese Medien sind laut Index vorhanden, lassen sich an aktuell erreichbaren Speicherorten aber nicht mehr finden. Prüfe zuerst den Medienindex und die betroffenen Pfade.",
                "Medienindex öffnen",
                OpenMediaIndexCommand,
                isProblem: true));
        }

        if (report.IndexSummary.StaleLocationCount > 0)
        {
            recommendationItems.Add(new LibraryHealthRecommendationItemViewModel(
                "Hohe Priorität",
                "Veraltete Indexorte bereinigen",
                $"{report.IndexSummary.StaleLocationCount:N0} aktuell markierte Indexorte existieren physisch nicht mehr. Eine Indexaktualisierung kann Verschiebungen oder Löschungen sauber nachziehen.",
                "Index aktualisieren",
                UpdateMediaIndexCommand,
                isWarning: true));
        }

        if (report.IndexSummary.UnconfiguredMediaCount > 0
            || issueKinds.Contains(LibraryHealthIssueKind.PathMissing)
            || issueKinds.Contains(LibraryHealthIssueKind.AmbiguousVolume)
            || issueKinds.Contains(LibraryHealthIssueKind.InvalidConfiguration))
        {
            recommendationItems.Add(new LibraryHealthRecommendationItemViewModel(
                "Prüfen",
                "Speicherorte korrigieren",
                "Mindestens ein Bibliotheks- oder Indexeintrag verweist auf einen fehlenden, mehrdeutigen oder nicht mehr konfigurierten Speicherort.",
                "Speicherorte öffnen",
                OpenSettingsCommand,
                isProblem: issueKinds.Contains(LibraryHealthIssueKind.PathMissing)
                           || issueKinds.Contains(LibraryHealthIssueKind.AmbiguousVolume)
                           || issueKinds.Contains(LibraryHealthIssueKind.InvalidConfiguration),
                isWarning: !issueKinds.Contains(LibraryHealthIssueKind.PathMissing)
                           && !issueKinds.Contains(LibraryHealthIssueKind.AmbiguousVolume)
                           && !issueKinds.Contains(LibraryHealthIssueKind.InvalidConfiguration)));
        }

        var metadataQualityCount =
            MissingCaptureDateCount
            + ConflictingCaptureDateCount
            + SuspiciousCaptureDateCount
            + MissingDimensionsCount
            + MetadataErrorCount;

        if (metadataQualityCount > 0)
        {
            recommendationItems.Add(new LibraryHealthRecommendationItemViewModel(
                "Bei Bedarf",
                "Metadatenqualität prüfen",
                $"{metadataQualityCount:N0} auffällige Bildmerkmale können Datumssortierung, Vorschauen oder spätere Projektvorschläge einschränken. Es wird nichts automatisch korrigiert.",
                "Metadaten öffnen",
                OpenMetadataCommand,
                isWarning: ConflictingCaptureDateCount > 0
                           || SuspiciousCaptureDateCount > 0
                           || MetadataErrorCount > 0));
        }

        if (report.IndexSummary.OfflineMediaCount > 0 || OfflineEndpointCount > 0)
        {
            recommendationItems.Add(new LibraryHealthRecommendationItemViewModel(
                "Hinweis",
                "Offline-Speicherorte bei Bedarf verbinden",
                $"{report.IndexSummary.OfflineMediaCount:N0} indexierte Medien liegen derzeit auf nicht verbundenen oder deaktivierten Speicherorten. Das wird ausdrücklich nicht als Datenverlust gewertet.",
                "Speicherorte öffnen",
                OpenSettingsCommand));
        }

        if (issueKinds.Contains(LibraryHealthIssueKind.ScanError))
        {
            recommendationItems.Insert(0, new LibraryHealthRecommendationItemViewModel(
                "Hohe Priorität",
                "Scan-Fehler untersuchen",
                "Mindestens eine Health-Prüfung konnte nicht vollständig ausgeführt werden. Das Diagnoseprotokoll enthält die technische Ursache.",
                "Protokoll öffnen",
                OpenDiagnosticsCommand,
                isProblem: true));
        }

        Recommendations = recommendationItems;

        HasOverallProblem = report.ProblemCount > 0;
        HasOverallWarning = !HasOverallProblem && report.WarningCount > 0;
        HasOverallHealthy = !HasOverallProblem && !HasOverallWarning;

        if (HasOverallProblem)
        {
            OverallHealthTitle = "Handlungsbedarf";
            OverallHealthMessage = $"{report.ProblemCount:N0} Problem(e) sollten geprüft werden. Elbwald führt keine automatische Reparatur durch.";
            OverallHealthHint = recommendationItems.Count > 0
                ? "Beginne mit der obersten empfohlenen Aktion."
                : "Öffne die Auffälligkeiten für weitere Details.";
            return;
        }

        if (HasOverallWarning)
        {
            OverallHealthTitle = "Prüfen empfohlen";
            OverallHealthMessage = $"Keine akuten Probleme, aber {report.WarningCount:N0} Warnung(en) verdienen Aufmerksamkeit.";
            OverallHealthHint = "Die Bibliothek bleibt nutzbar; die Empfehlungen helfen beim Aufräumen.";
            return;
        }

        OverallHealthTitle = "Gesund";
        OverallHealthMessage = report.IndexSummary.OfflineMediaCount > 0 || OfflineEndpointCount > 0
            ? "Keine Probleme oder Warnungen gefunden. Offline-Speicherorte werden korrekt als vorübergehend nicht verfügbar behandelt."
            : "Keine Probleme oder Warnungen gefunden. Speicherorte, Medienindex und Metadatenqualität sind unauffällig.";
        OverallHealthHint = recommendationItems.Count > 0
            ? "Es gibt nur optionale Hinweise; keine Reparatur ist erforderlich."
            : "Aktuell ist keine Aktion erforderlich.";
    }

    private sealed record LibraryHealthSnapshotLoadState(
        StorageSettings Settings,
        MediaIndexSummary MediaIndexSummary,
        LibraryHealthSnapshot? Snapshot,
        LibraryHealthSnapshotFreshness? Freshness,
        bool AvailabilityChanged,
        LibraryHealthReportProjection? Projection);

    private sealed record LibraryHealthCompletedScanState(
        MediaIndexSummary MediaIndexSummary,
        LibraryHealthReportProjection Projection,
        string? SnapshotWarning);

    private sealed record LibraryHealthReportProjection(
        IReadOnlyList<LibraryHealthEndpointItemViewModel> Endpoints,
        IReadOnlyList<LibraryHealthIssueItemViewModel> Issues);

    private sealed record IssueCounts(int Warnings, int Problems);

}
