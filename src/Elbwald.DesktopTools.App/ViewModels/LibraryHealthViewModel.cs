using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class LibraryHealthViewModel : ObservableObject
{
    private readonly ILibraryHealthService _libraryHealthService;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private IReadOnlyList<LibraryHealthEndpointItemViewModel> endpoints =
        Array.Empty<LibraryHealthEndpointItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<LibraryHealthIssueItemViewModel> issues =
        Array.Empty<LibraryHealthIssueItemViewModel>();

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Bibliotheksstatus wird geladen …";

    [ObservableProperty]
    private string lastScanMessage = "Noch kein Scan ausgeführt.";

    [ObservableProperty]
    private int totalEndpointCount;

    [ObservableProperty]
    private int availableEndpointCount;

    [ObservableProperty]
    private int warningCount;

    [ObservableProperty]
    private int problemCount;

    [ObservableProperty]
    private int indexedMediaCount;

    [ObservableProperty]
    private int presentIndexedMediaCount;

    [ObservableProperty]
    private int orphanedIndexedMediaCount;

    [ObservableProperty]
    private int missingCaptureDateIndexCount;

    [ObservableProperty]
    private string indexLastUpdatedMessage = "Medienindex wird geprüft …";

    public LibraryHealthViewModel(
        ILibraryHealthService libraryHealthService,
        IStorageSettingsStore storageSettingsStore,
        INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(libraryHealthService);
        ArgumentNullException.ThrowIfNull(storageSettingsStore);
        ArgumentNullException.ThrowIfNull(navigationService);

        _libraryHealthService = libraryHealthService;
        _storageSettingsStore = storageSettingsStore;
        _navigationService = navigationService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        OpenSettingsCommand = new RelayCommand(() => _navigationService.NavigateTo("settings"));
        OpenDiagnosticsCommand = new RelayCommand(() => _navigationService.NavigateTo("diagnostics"));
        OpenMediaIndexCommand = new RelayCommand(() => _navigationService.NavigateTo("media-index"));

        _ = RefreshAsync();
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand OpenDiagnosticsCommand { get; }

    public IRelayCommand OpenMediaIndexCommand { get; }

    public bool HasIndexData => IndexedMediaCount > 0;

    public string IndexAvailabilitySummary =>
        IndexedMediaCount == 0
            ? "Noch keine Medien indexiert"
            : $"{PresentIndexedMediaCount:N0} von {IndexedMediaCount:N0} aktuell erreichbar";

    public bool HasEndpoints => Endpoints.Count > 0;

    public bool HasIssues => Issues.Count > 0;

    public bool HasNoIssues => !IsBusy && Issues.Count == 0;

    public bool HasNoEndpoints => !IsBusy && Endpoints.Count == 0;

    public string EndpointSummary =>
        TotalEndpointCount == 1
            ? "1 konfigurierter Endpunkt"
            : $"{TotalEndpointCount:N0} konfigurierte Endpunkte";

    public string AvailabilitySummary =>
        AvailableEndpointCount == 1
            ? "1 verfügbar"
            : $"{AvailableEndpointCount:N0} verfügbar";

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoIssues));
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
    }

    partial void OnTotalEndpointCountChanged(int value)
    {
        OnPropertyChanged(nameof(EndpointSummary));
    }

    partial void OnAvailableEndpointCountChanged(int value)
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

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Bibliotheksstatus wird aktualisiert …";

        try
        {
            var settings = await _storageSettingsStore.LoadAsync();
            var enabledEndpoints = settings.Endpoints.Where(endpoint => endpoint.Enabled).ToArray();

            var report = await _libraryHealthService.ScanAsync(
                settings with { Endpoints = enabledEndpoints });

            var endpointIssues = report.Issues
                .GroupBy(issue => issue.EndpointId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        Warnings = group.Count(issue => issue.Severity == LibraryHealthSeverity.Warning),
                        Problems = group.Count(issue => issue.Severity == LibraryHealthSeverity.Problem)
                    },
                    StringComparer.OrdinalIgnoreCase);

            Endpoints = report.Endpoints
                .Select(result =>
                {
                    var counts = endpointIssues.TryGetValue(result.Endpoint.Id, out var grouped)
                        ? grouped
                        : null;

                    return new LibraryHealthEndpointItemViewModel(
                        result,
                        counts?.Warnings ?? 0,
                        counts?.Problems ?? 0);
                })
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            var endpointNames = report.Endpoints
                .ToDictionary(
                    result => result.Endpoint.Id,
                    result => result.Endpoint.Name,
                    StringComparer.OrdinalIgnoreCase);

            Issues = report.Issues
                .Select(issue => new LibraryHealthIssueItemViewModel(
                    endpointNames.TryGetValue(issue.EndpointId, out var endpointName)
                        ? endpointName
                        : string.Equals(issue.EndpointId, "media-index", StringComparison.OrdinalIgnoreCase)
                            ? "Medienindex"
                            : issue.EndpointId,
                    issue))
                .ToArray();

            IndexedMediaCount = report.IndexSummary.IndexedMediaCount;
            PresentIndexedMediaCount = report.IndexSummary.PresentMediaCount;
            OrphanedIndexedMediaCount = report.IndexSummary.OrphanedMediaCount;
            MissingCaptureDateIndexCount = report.IndexSummary.MissingCaptureDateCount;
            IndexLastUpdatedMessage = report.IndexSummary.LastIndexedUtc is { } lastIndexed
                ? $"Index zuletzt aktualisiert: {lastIndexed.ToLocalTime():dd.MM.yyyy HH:mm}"
                : "Medienindex wurde noch nicht aufgebaut.";

            TotalEndpointCount = report.Endpoints.Count;
            AvailableEndpointCount = report.Endpoints.Count(endpoint => endpoint.StorageStatus == StorageLocationStatus.Available);
            WarningCount = report.WarningCount;
            ProblemCount = report.ProblemCount;

            var duration = report.CompletedAtUtc - report.StartedAtUtc;
            LastScanMessage = $"Letzter Scan: {report.CompletedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm} · Dauer {duration:mm\\:ss}";

            StatusMessage = enabledEndpoints.Length == 0
                ? report.Issues.Count == 0
                    ? "Medienindex geprüft. Für einen Dateisystem-Scan ist noch kein aktiver Speicherort konfiguriert."
                    : $"Medienindex geprüft · {report.ProblemCount:N0} Problem(e) · {report.WarningCount:N0} Warnung(en). Für den Dateisystem-Scan fehlt ein aktiver Speicherort."
                : report.Issues.Count == 0
                    ? "Keine Auffälligkeiten gefunden. Speicherorte und Medienindex wurden read-only geprüft."
                    : $"{report.Endpoints.Count:N0} Endpunkt(e) + Medienindex geprüft · {report.ProblemCount:N0} Problem(e) · {report.WarningCount:N0} Warnung(en).";
        }
        catch (Exception exception)
        {
            Endpoints = Array.Empty<LibraryHealthEndpointItemViewModel>();
            Issues = Array.Empty<LibraryHealthIssueItemViewModel>();
            TotalEndpointCount = 0;
            AvailableEndpointCount = 0;
            WarningCount = 0;
            ProblemCount = 0;
            IndexedMediaCount = 0;
            PresentIndexedMediaCount = 0;
            OrphanedIndexedMediaCount = 0;
            MissingCaptureDateIndexCount = 0;
            IndexLastUpdatedMessage = "Medienindex konnte nicht geprüft werden.";
            LastScanMessage = "Der letzte Scan wurde nicht erfolgreich abgeschlossen.";
            StatusMessage = "Der Bibliotheksstatus konnte nicht geladen werden: " + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
