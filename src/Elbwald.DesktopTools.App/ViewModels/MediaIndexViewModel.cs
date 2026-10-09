using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MediaIndexViewModel : ObservableObject
{
    private readonly IMediaIndexService _mediaIndexService;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly INavigationService _navigationService;
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _elapsedTimer;
    private CancellationTokenSource? _indexCancellation;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Indexstatus wird geladen …";

    [ObservableProperty]
    private string lastIndexedText = "Noch kein Indexlauf gespeichert.";

    [ObservableProperty]
    private string elapsedText = "00:00";

    [ObservableProperty]
    private int itemCount;

    [ObservableProperty]
    private int presentLocationCount;

    [ObservableProperty]
    private int historicalLocationCount;

    [ObservableProperty]
    private int endpointCount;

    [ObservableProperty]
    private int filesDiscovered;

    [ObservableProperty]
    private int filesIndexed;

    [ObservableProperty]
    private int newItems;

    [ObservableProperty]
    private int newLocations;

    [ObservableProperty]
    private int reusedHashes;

    [ObservableProperty]
    private int locationsMarkedMissing;

    [ObservableProperty]
    private int relocationCount;

    [ObservableProperty]
    private IReadOnlyList<MediaIndexRelocationItemViewModel> relocations =
        Array.Empty<MediaIndexRelocationItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<MediaIndexIssueItemViewModel> issues =
        Array.Empty<MediaIndexIssueItemViewModel>();

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string selectedPresenceFilter = "Alle";

    [ObservableProperty]
    private IReadOnlyList<MediaIndexBrowseItemViewModel> searchResults =
        Array.Empty<MediaIndexBrowseItemViewModel>();

    [ObservableProperty]
    private MediaIndexBrowseItemViewModel? selectedSearchResult;

    [ObservableProperty]
    private string searchStatusMessage = "Noch keine Indexsuche ausgeführt.";

    [ObservableProperty]
    private bool hasSelectedMedia;

    [ObservableProperty]
    private string selectedMediaTitle = string.Empty;

    [ObservableProperty]
    private string selectedMediaId = string.Empty;

    [ObservableProperty]
    private string selectedMediaHash = string.Empty;

    [ObservableProperty]
    private string selectedMediaType = string.Empty;

    [ObservableProperty]
    private string selectedMediaSize = string.Empty;

    [ObservableProperty]
    private string selectedMediaSeen = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<MediaIndexLocationItemViewModel> selectedMediaLocations =
        Array.Empty<MediaIndexLocationItemViewModel>();

    public MediaIndexViewModel(
        IMediaIndexService mediaIndexService,
        IStorageSettingsStore storageSettingsStore,
        INavigationService navigationService,
        IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        ArgumentNullException.ThrowIfNull(storageSettingsStore);
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(appPaths);

        _mediaIndexService = mediaIndexService;
        _storageSettingsStore = storageSettingsStore;
        _navigationService = navigationService;

        DatabasePath = Path.Combine(
            appPaths.DatabaseDirectory,
            "media-index.db");

        RefreshSummaryCommand = new AsyncRelayCommand(
            RefreshSummaryAsync,
            () => !IsBusy);

        UpdateIndexCommand = new AsyncRelayCommand(
            UpdateIndexAsync,
            () => !IsBusy);

        CancelCommand = new RelayCommand(
            CancelIndex,
            () => IsBusy);

        OpenSettingsCommand = new RelayCommand(
            () => _navigationService.NavigateTo("settings"));

        SearchCommand = new AsyncRelayCommand(
            SearchAsync,
            () => !IsBusy);

        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (_, _) => UpdateElapsedText();

        _ = RefreshSummaryAsync();
    }

    public string DatabasePath { get; }

    public IAsyncRelayCommand RefreshSummaryCommand { get; }

    public IAsyncRelayCommand UpdateIndexCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IAsyncRelayCommand SearchCommand { get; }

    public IReadOnlyList<string> PresenceFilters { get; } =
        new[] { "Alle", "Aktuell", "Historisch" };

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool HasNoSearchResults => !IsBusy && SearchResults.Count == 0;

    public bool HasNoSelectedMedia => !HasSelectedMedia;

    public bool HasIssues => Issues.Count > 0;

    public bool HasNoIssues => !IsBusy && Issues.Count == 0;

    public bool HasRelocations => Relocations.Count > 0;

    public bool HasRunDetails =>
        EndpointCount > 0
        || FilesDiscovered > 0
        || FilesIndexed > 0
        || NewItems > 0
        || NewLocations > 0
        || ReusedHashes > 0
        || LocationsMarkedMissing > 0
        || RelocationCount > 0;

    partial void OnIsBusyChanged(bool value)
    {
        RefreshSummaryCommand.NotifyCanExecuteChanged();
        UpdateIndexCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoIssues));
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    partial void OnIssuesChanged(IReadOnlyList<MediaIndexIssueItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(HasNoIssues));
    }

    partial void OnRelocationsChanged(IReadOnlyList<MediaIndexRelocationItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasRelocations));
    }

    partial void OnSearchResultsChanged(IReadOnlyList<MediaIndexBrowseItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    partial void OnSelectedSearchResultChanged(MediaIndexBrowseItemViewModel? value)
    {
        if (value is not null)
        {
            _ = OpenMediaDetailsAsync(value.Id);
        }
    }

    partial void OnHasSelectedMediaChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoSelectedMedia));
    }

    partial void OnEndpointCountChanged(int value) => OnPropertyChanged(nameof(HasRunDetails));
    partial void OnFilesDiscoveredChanged(int value) => OnPropertyChanged(nameof(HasRunDetails));
    partial void OnFilesIndexedChanged(int value) => OnPropertyChanged(nameof(HasRunDetails));
    partial void OnNewItemsChanged(int value) => OnPropertyChanged(nameof(HasRunDetails));
    partial void OnNewLocationsChanged(int value) => OnPropertyChanged(nameof(HasRunDetails));
    partial void OnReusedHashesChanged(int value) => OnPropertyChanged(nameof(HasRunDetails));
    partial void OnLocationsMarkedMissingChanged(int value) => OnPropertyChanged(nameof(HasRunDetails));
    partial void OnRelocationCountChanged(int value) => OnPropertyChanged(nameof(HasRunDetails));

    private async Task RefreshSummaryAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Indexstatus wird geladen …";

        try
        {
            var summary = await _mediaIndexService.GetSummaryAsync();
            ApplySummary(summary);
            StatusMessage = summary.LastIndexedUtc is null
                ? "Der Medienindex ist bereit, wurde aber noch nicht aktualisiert."
                : "Indexstatus geladen. Ein neuer Lauf verändert keine Mediendateien.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Der Indexstatus konnte nicht gelesen werden: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UpdateIndexAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Issues = Array.Empty<MediaIndexIssueItemViewModel>();
        Relocations = Array.Empty<MediaIndexRelocationItemViewModel>();
        ResetRunDetails();
        StatusMessage = "Medienindex wird read-only aktualisiert …";

        _indexCancellation?.Dispose();
        _indexCancellation = new CancellationTokenSource();

        _stopwatch.Restart();
        UpdateElapsedText();
        _elapsedTimer.Start();

        try
        {
            var settings = await _storageSettingsStore.LoadAsync(
                _indexCancellation.Token);

            var enabledCount = settings.Endpoints.Count(endpoint => endpoint.Enabled);
            if (enabledCount == 0)
            {
                StatusMessage = "Es sind keine aktivierten Speicher-Endpunkte konfiguriert.";
                return;
            }

            var result = await _mediaIndexService.IndexAsync(
                settings,
                _indexCancellation.Token);

            EndpointCount = result.EndpointCount;
            FilesDiscovered = result.FilesDiscovered;
            FilesIndexed = result.FilesIndexed;
            NewItems = result.NewItems;
            NewLocations = result.NewLocations;
            ReusedHashes = result.ReusedHashes;
            LocationsMarkedMissing = result.LocationsMarkedMissing;
            RelocationCount = result.RelocationCount;

            var endpointNames = settings.Endpoints
                .GroupBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name,
                    StringComparer.OrdinalIgnoreCase);

            Relocations = result.Relocations
                .Select(relocation => new MediaIndexRelocationItemViewModel(
                    relocation,
                    endpointNames))
                .ToArray();

            Issues = result.Issues
                .Select(issue => new MediaIndexIssueItemViewModel(issue))
                .ToArray();

            var summary = await _mediaIndexService.GetSummaryAsync(
                _indexCancellation.Token);
            ApplySummary(summary);

            StatusMessage = result.CompletedWithIssues
                ? $"Indexlauf abgeschlossen mit {result.Issues.Count:N0} Hinweis(en)/Problem(en)."
                : "Indexlauf erfolgreich abgeschlossen.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Indexlauf abgebrochen. Bereits geschriebene Indexdaten bleiben erhalten; Mediendateien wurden nicht verändert.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Der Indexlauf ist fehlgeschlagen: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            _elapsedTimer.Stop();
            _stopwatch.Stop();
            UpdateElapsedText();
            IsBusy = false;
        }
    }

    private async Task SearchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        HasSelectedMedia = false;
        SelectedMediaLocations = Array.Empty<MediaIndexLocationItemViewModel>();
        SearchStatusMessage = "Index wird durchsucht …";

        try
        {
            var settings = await _storageSettingsStore.LoadAsync();
            var endpointNames = settings.Endpoints
                .GroupBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name,
                    StringComparer.OrdinalIgnoreCase);

            var query = new MediaIndexSearchQuery(
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                PresentOnly: SelectedPresenceFilter == "Aktuell",
                HistoricalOnly: SelectedPresenceFilter == "Historisch",
                Limit: 200);

            var results = await _mediaIndexService.SearchAsync(query);
            SearchResults = results
                .Select(result => new MediaIndexBrowseItemViewModel(result, endpointNames))
                .ToArray();

            SearchStatusMessage = SearchResults.Count == 0
                ? "Keine passenden Medien im Index gefunden."
                : $"{SearchResults.Count:N0} Treffer · maximal 200 werden angezeigt.";
        }
        catch (Exception exception)
        {
            SearchResults = Array.Empty<MediaIndexBrowseItemViewModel>();
            SearchStatusMessage = "Der Index konnte nicht durchsucht werden: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenMediaDetailsAsync(string? mediaItemId)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(mediaItemId))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var item = await _mediaIndexService.GetItemAsync(mediaItemId);
            if (item is null)
            {
                HasSelectedMedia = false;
                SearchStatusMessage = "Das ausgewählte Medium ist nicht mehr im Index vorhanden.";
                return;
            }

            var settings = await _storageSettingsStore.LoadAsync();
            var endpointNames = settings.Endpoints
                .GroupBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name,
                    StringComparer.OrdinalIgnoreCase);

            var locations = await _mediaIndexService.GetLocationsAsync(item.Id);
            SelectedMediaTitle = locations.FirstOrDefault(location => location.IsPresent)?.RelativePath
                ?? locations.FirstOrDefault()?.RelativePath
                ?? item.Id;
            SelectedMediaId = item.Id;
            SelectedMediaHash = item.Sha256;
            SelectedMediaType = MediaIndexBrowseItemViewModel.GetMediaTypeLabel(item.MediaType);
            SelectedMediaSize = MediaIndexBrowseItemViewModel.FormatBytes(item.Length);
            SelectedMediaSeen = $"Erstmals {item.FirstSeenUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss} · zuletzt {item.LastSeenUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}";
            SelectedMediaLocations = locations
                .Select(location => new MediaIndexLocationItemViewModel(location, endpointNames))
                .ToArray();
            HasSelectedMedia = true;
        }
        catch (Exception exception)
        {
            HasSelectedMedia = false;
            SearchStatusMessage = "Details konnten nicht geladen werden: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelIndex()
    {
        _indexCancellation?.Cancel();
    }

    private void ApplySummary(MediaIndexSummary summary)
    {
        ItemCount = summary.ItemCount;
        PresentLocationCount = summary.PresentLocationCount;
        HistoricalLocationCount = summary.HistoricalLocationCount;
        LastIndexedText = summary.LastIndexedUtc is null
            ? "Noch kein Indexlauf gespeichert."
            : $"Letzter Indexlauf: {summary.LastIndexedUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm:ss}";
    }

    private void ResetRunDetails()
    {
        EndpointCount = 0;
        FilesDiscovered = 0;
        FilesIndexed = 0;
        NewItems = 0;
        NewLocations = 0;
        ReusedHashes = 0;
        LocationsMarkedMissing = 0;
        RelocationCount = 0;
        Relocations = Array.Empty<MediaIndexRelocationItemViewModel>();
    }

    private void UpdateElapsedText()
    {
        var elapsed = _stopwatch.Elapsed;
        ElapsedText = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    }
}
