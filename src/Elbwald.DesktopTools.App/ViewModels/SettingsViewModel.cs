using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.UI.Formatting;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class SettingsViewModel
    : ObservableObject
{
    private readonly IMediaAnalysisCache _analysisCache;
    private readonly IMediaThumbnailService _thumbnailService;
    private readonly IMediaSourcePicker _sourcePicker;
    private readonly IAppPaths _appPaths;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageLocationResolver;

    private readonly Stopwatch _operationStopwatch = new();
    private readonly DispatcherTimer _durationTimer;

    private StorageSettings _storageSettings = StorageSettings.Default;
    private bool _isBusy;
    private bool _isImportLocationConfigured;
    private bool _isLibraryLocationConfigured;

    private string _operationStatus = "Bereit.";
    private string _operationDuration = "Dauer: 00:00:00";
    private string _analysisCacheSummary = "Wird gelesen …";
    private string _analysisCacheLocation = string.Empty;
    private string _thumbnailCacheSummary = "Wird gelesen …";
    private string _thumbnailCacheLocation = string.Empty;
    private string _totalCacheSize = "0 B";
    private string _importLocationSummary = "Nicht konfiguriert";
    private string _importLocationDetails = "Noch keine Import-Wartehalle festgelegt.";
    private string _importLocationVolumeId = string.Empty;
    private string _libraryLocationSummary = "Nicht konfiguriert";
    private string _libraryLocationDetails = "Noch keine Medienbibliothek festgelegt.";
    private string _libraryLocationVolumeId = string.Empty;

    public SettingsViewModel(
        IMediaAnalysisCache analysisCache,
        IMediaThumbnailService thumbnailService,
        IMediaSourcePicker sourcePicker,
        IAppPaths appPaths,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver)
    {
        ArgumentNullException.ThrowIfNull(analysisCache);
        ArgumentNullException.ThrowIfNull(thumbnailService);
        ArgumentNullException.ThrowIfNull(sourcePicker);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(storageSettingsStore);
        ArgumentNullException.ThrowIfNull(storageLocationResolver);

        _analysisCache = analysisCache;
        _thumbnailService = thumbnailService;
        _sourcePicker = sourcePicker;
        _appPaths = appPaths;
        _storageSettingsStore = storageSettingsStore;
        _storageLocationResolver = storageLocationResolver;

        _durationTimer =
            new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

        _durationTimer.Tick += (_, _) => UpdateDuration();

        RefreshCacheCommand =
            new AsyncRelayCommand(
                RefreshCacheAsync,
                CanManageSettings);

        ClearAnalysisCacheCommand =
            new AsyncRelayCommand(
                ClearAnalysisCacheAsync,
                CanManageSettings);

        ClearThumbnailCacheCommand =
            new AsyncRelayCommand(
                ClearThumbnailCacheAsync,
                CanManageSettings);

        ClearAllCachesCommand =
            new AsyncRelayCommand(
                ClearAllCachesAsync,
                CanManageSettings);

        RefreshStorageCommand =
            new AsyncRelayCommand(
                RefreshStorageAsync,
                CanManageSettings);

        SelectImportLocationCommand =
            new AsyncRelayCommand(
                SelectImportLocationAsync,
                CanManageSettings);

        ClearImportLocationCommand =
            new AsyncRelayCommand(
                ClearImportLocationAsync,
                () => CanManageSettings() && IsImportLocationConfigured);

        SelectLibraryLocationCommand =
            new AsyncRelayCommand(
                SelectLibraryLocationAsync,
                CanManageSettings);

        ClearLibraryLocationCommand =
            new AsyncRelayCommand(
                ClearLibraryLocationAsync,
                () => CanManageSettings() && IsLibraryLocationConfigured);

        _ = InitializeAsync();
    }

    public IAsyncRelayCommand RefreshCacheCommand { get; }
    public IAsyncRelayCommand ClearAnalysisCacheCommand { get; }
    public IAsyncRelayCommand ClearThumbnailCacheCommand { get; }
    public IAsyncRelayCommand ClearAllCachesCommand { get; }
    public IAsyncRelayCommand RefreshStorageCommand { get; }
    public IAsyncRelayCommand SelectImportLocationCommand { get; }
    public IAsyncRelayCommand ClearImportLocationCommand { get; }
    public IAsyncRelayCommand SelectLibraryLocationCommand { get; }
    public IAsyncRelayCommand ClearLibraryLocationCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public bool IsImportLocationConfigured
    {
        get => _isImportLocationConfigured;
        private set
        {
            if (SetProperty(ref _isImportLocationConfigured, value))
            {
                ClearImportLocationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLibraryLocationConfigured
    {
        get => _isLibraryLocationConfigured;
        private set
        {
            if (SetProperty(ref _isLibraryLocationConfigured, value))
            {
                ClearLibraryLocationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string OperationStatus
    {
        get => _operationStatus;
        private set => SetProperty(ref _operationStatus, value);
    }

    public string OperationDuration
    {
        get => _operationDuration;
        private set => SetProperty(ref _operationDuration, value);
    }

    public string AnalysisCacheSummary
    {
        get => _analysisCacheSummary;
        private set => SetProperty(ref _analysisCacheSummary, value);
    }

    public string AnalysisCacheLocation
    {
        get => _analysisCacheLocation;
        private set => SetProperty(ref _analysisCacheLocation, value);
    }

    public string ThumbnailCacheSummary
    {
        get => _thumbnailCacheSummary;
        private set => SetProperty(ref _thumbnailCacheSummary, value);
    }

    public string ThumbnailCacheLocation
    {
        get => _thumbnailCacheLocation;
        private set => SetProperty(ref _thumbnailCacheLocation, value);
    }

    public string TotalCacheSize
    {
        get => _totalCacheSize;
        private set => SetProperty(ref _totalCacheSize, value);
    }

    public string ImportLocationSummary
    {
        get => _importLocationSummary;
        private set => SetProperty(ref _importLocationSummary, value);
    }

    public string ImportLocationDetails
    {
        get => _importLocationDetails;
        private set => SetProperty(ref _importLocationDetails, value);
    }

    public string ImportLocationVolumeId
    {
        get => _importLocationVolumeId;
        private set => SetProperty(ref _importLocationVolumeId, value);
    }

    public string LibraryLocationSummary
    {
        get => _libraryLocationSummary;
        private set => SetProperty(ref _libraryLocationSummary, value);
    }

    public string LibraryLocationDetails
    {
        get => _libraryLocationDetails;
        private set => SetProperty(ref _libraryLocationDetails, value);
    }

    public string LibraryLocationVolumeId
    {
        get => _libraryLocationVolumeId;
        private set => SetProperty(ref _libraryLocationVolumeId, value);
    }

    public string AppDataMode =>
        _appPaths.IsPortable
            ? "Portable"
            : "Installiert";

    public string AppDataModeDetails =>
        _appPaths.IsPortable
            ? "portable.flag wurde neben der Anwendung erkannt. Technische App-Daten bleiben relativ zur Anwendung."
            : "Technische App-Daten liegen im lokalen Benutzerprofil. Eine portable Installation wird über portable.flag neben der Anwendung aktiviert.";

    public string ApplicationDataDirectory => _appPaths.ApplicationDataDirectory;
    public string ConfigDirectory => _appPaths.ConfigDirectory;
    public string DatabaseDirectory => _appPaths.DatabaseDirectory;
    public string DiagnosticsDirectory => _appPaths.DiagnosticsDirectory;
    public string RecoveryDirectory => _appPaths.RecoveryDirectory;
    public string CacheDirectory => _appPaths.CacheDirectory;
    public string PortableModeMarkerPath => _appPaths.PortableModeMarkerPath;

    public string PortableModeMarkerDisplay =>
        "Portable-Schalter: " + _appPaths.PortableModeMarkerPath;

    public string PerformanceProfile => "Sicheres Standardprofil";

    public string PerformanceDetails =>
        "Read-only Analyse · SQLite-Metadaten-Cache · lazy Thumbnails · "
        + "max. 500 Inspector-Treffer · max. 12 Galerie-Vorschauen";

    public string DataPolicy =>
        "Cache-Dateien sind vollständig wiederherstellbar und dürfen jederzeit "
        + "neu aufgebaut werden. Originalmedien und Recovery-Daten sind keine Cache-Daten.";

    public string StorageIdentityPolicy =>
        "Medienorte werden als stabile Laufwerkskennung plus relativer Pfad gespeichert. "
        + "Laufwerksbuchstaben und Linux-Mountpoints dienen nur zur aktuellen Auflösung. "
        + "Stimmt die Kennung nicht eindeutig, verwendet Elbwald den Ort nicht.";

    private async Task InitializeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        BeginOperation("Einstellungen werden gelesen …");

        try
        {
            await LoadCacheStatisticsAsync();
            await LoadStorageSettingsAsync();
            EndOperation("Einstellungen geladen.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or System.Text.Json.JsonException)
        {
            EndOperation("Einstellungen konnten nicht vollständig gelesen werden: " + exception.Message);
        }
    }

    private bool CanManageSettings() => !IsBusy;

    private async Task RefreshCacheAsync()
    {
        if (!CanManageSettings())
        {
            return;
        }

        BeginOperation("Cache-Status wird gelesen …");

        try
        {
            await LoadCacheStatisticsAsync();
            EndOperation("Cache-Status aktualisiert.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            EndOperation("Cache-Status konnte nicht vollständig gelesen werden: " + exception.Message);
        }
    }

    private async Task RefreshStorageAsync()
    {
        if (!CanManageSettings())
        {
            return;
        }

        BeginOperation("Speicherorte werden geprüft …");

        try
        {
            await LoadStorageSettingsAsync();
            EndOperation("Speicherorte anhand ihrer Laufwerkskennungen geprüft.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or System.Text.Json.JsonException)
        {
            EndOperation("Speicherorte konnten nicht vollständig gelesen werden: " + exception.Message);
        }
    }

    private async Task SelectImportLocationAsync()
    {
        await SelectStorageLocationAsync(isImportLocation: true);
    }

    private async Task SelectLibraryLocationAsync()
    {
        await SelectStorageLocationAsync(isImportLocation: false);
    }

    private async Task SelectStorageLocationAsync(bool isImportLocation)
    {
        if (!CanManageSettings())
        {
            return;
        }

        string? selectedPath;

        try
        {
            selectedPath = await _sourcePicker.PickFolderAsync();
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            OperationStatus = "Ordnerauswahl fehlgeschlagen: " + exception.Message;
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        BeginOperation("Stabile Laufwerkskennung wird ermittelt …");

        try
        {
            var location = _storageLocationResolver.Capture(selectedPath);

            var endpoint = new StorageEndpoint(
                isImportLocation
                    ? StorageEndpoint.DefaultImportId
                    : StorageEndpoint.DefaultLibraryId,
                isImportLocation
                    ? "Import-Wartehalle"
                    : "Medienbibliothek",
                isImportLocation
                    ? StorageEndpointKind.Import
                    : StorageEndpointKind.MediaLibrary,
                location);

            _storageSettings = _storageSettings.WithEndpoint(endpoint);

            await _storageSettingsStore.SaveAsync(_storageSettings);
            ApplyStoragePresentation();

            EndOperation(
                isImportLocation
                    ? "Import-Wartehalle gespeichert. Die Laufwerkskennung ist maßgeblich."
                    : "Medienbibliothek gespeichert. Die Laufwerkskennung ist maßgeblich.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            EndOperation("Speicherort wurde nicht übernommen: " + exception.Message);
        }
    }

    private async Task ClearImportLocationAsync()
    {
        await ClearStorageLocationAsync(isImportLocation: true);
    }

    private async Task ClearLibraryLocationAsync()
    {
        await ClearStorageLocationAsync(isImportLocation: false);
    }

    private async Task ClearStorageLocationAsync(bool isImportLocation)
    {
        if (!CanManageSettings())
        {
            return;
        }

        BeginOperation("Speicherort wird aus der Konfiguration entfernt …");

        try
        {
            _storageSettings = _storageSettings.WithoutEndpoint(
                isImportLocation
                    ? StorageEndpoint.DefaultImportId
                    : StorageEndpoint.DefaultLibraryId);

            await _storageSettingsStore.SaveAsync(_storageSettings);
            ApplyStoragePresentation();

            EndOperation("Speicherort aus der Konfiguration entfernt. Es wurden keine Mediendateien verändert.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            EndOperation("Speicherort konnte nicht entfernt werden: " + exception.Message);
        }
    }

    private async Task ClearAnalysisCacheAsync()
    {
        if (!CanManageSettings())
        {
            return;
        }

        BeginOperation("SQLite-Analyse-Cache wird geleert …");

        try
        {
            await _analysisCache.ClearAsync();
            await LoadCacheStatisticsAsync();
            EndOperation("Analyse-Cache geleert. Originalmedien wurden nicht verändert.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            EndOperation("Analyse-Cache konnte nicht geleert werden: " + exception.Message);
        }
    }

    private async Task ClearThumbnailCacheAsync()
    {
        if (!CanManageSettings())
        {
            return;
        }

        BeginOperation("Thumbnail-Cache wird geleert …");

        try
        {
            await _thumbnailService.ClearCacheAsync();
            await LoadCacheStatisticsAsync();
            EndOperation("Thumbnail-Cache geleert. Originalmedien wurden nicht verändert.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            EndOperation("Thumbnail-Cache konnte nicht geleert werden: " + exception.Message);
        }
    }

    private async Task ClearAllCachesAsync()
    {
        if (!CanManageSettings())
        {
            return;
        }

        BeginOperation("Alle Medien-Caches werden geleert …");

        try
        {
            await _analysisCache.ClearAsync();
            await _thumbnailService.ClearCacheAsync();
            await LoadCacheStatisticsAsync();
            EndOperation("Alle Medien-Caches geleert. Originalmedien und Recovery-Daten blieben unangetastet.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            EndOperation("Die Caches konnten nicht vollständig geleert werden: " + exception.Message);
        }
    }

    private async Task LoadStorageSettingsAsync()
    {
        _storageSettings = await _storageSettingsStore.LoadAsync();
        ApplyStoragePresentation();
    }

    private void ApplyStoragePresentation()
    {
        ApplyStorageLocationPresentation(
            _storageSettings.Find(StorageEndpoint.DefaultImportId)?.Location,
            isImportLocation: true);

        ApplyStorageLocationPresentation(
            _storageSettings.Find(StorageEndpoint.DefaultLibraryId)?.Location,
            isImportLocation: false);
    }

    private void ApplyStorageLocationPresentation(
        StorageLocation? location,
        bool isImportLocation)
    {
        if (location is null)
        {
            if (isImportLocation)
            {
                IsImportLocationConfigured = false;
                ImportLocationSummary = "Nicht konfiguriert";
                ImportLocationDetails = "Noch keine Import-Wartehalle festgelegt.";
                ImportLocationVolumeId = string.Empty;
            }
            else
            {
                IsLibraryLocationConfigured = false;
                LibraryLocationSummary = "Nicht konfiguriert";
                LibraryLocationDetails = "Noch keine Medienbibliothek festgelegt.";
                LibraryLocationVolumeId = string.Empty;
            }

            return;
        }

        var resolution = _storageLocationResolver.Resolve(location);
        var summary = BuildResolutionSummary(resolution);
        var details = resolution.ResolvedPath
            ?? location.LastKnownAbsolutePath
            ?? "Aktueller Pfad nicht verfügbar";

        if (isImportLocation)
        {
            IsImportLocationConfigured = true;
            ImportLocationSummary = summary;
            ImportLocationDetails = details;
            ImportLocationVolumeId = location.VolumeId;
        }
        else
        {
            IsLibraryLocationConfigured = true;
            LibraryLocationSummary = summary;
            LibraryLocationDetails = details;
            LibraryLocationVolumeId = location.VolumeId;
        }
    }

    private static string BuildResolutionSummary(StorageLocationResolution resolution)
    {
        return resolution.Status switch
        {
            StorageLocationStatus.Available => "Verfügbar · Kennung bestätigt",
            StorageLocationStatus.VolumeUnavailable => "Laufwerk nicht verbunden",
            StorageLocationStatus.PathMissing => "Laufwerk erkannt · Unterordner fehlt",
            StorageLocationStatus.AmbiguousVolume => "Mehrdeutige Laufwerkskennung · gesperrt",
            StorageLocationStatus.InvalidConfiguration => "Ungültige Konfiguration · gesperrt",
            _ => "Nicht verfügbar"
        };
    }

    private async Task LoadCacheStatisticsAsync()
    {
        var analysis = await _analysisCache.GetStatisticsAsync();
        var thumbnails = await _thumbnailService.GetCacheStatisticsAsync();

        AnalysisCacheLocation = analysis.Location;
        ThumbnailCacheLocation = thumbnails.Location;

        AnalysisCacheSummary = analysis.IsAvailable
            ? $"{analysis.EntryCount:N0} Metadatensätze · {FormatBytes(analysis.SizeBytes)}"
            : BuildUnavailableLabel(analysis.Message);

        ThumbnailCacheSummary = thumbnails.IsAvailable
            ? $"{thumbnails.EntryCount:N0} Thumbnails · {FormatBytes(thumbnails.SizeBytes)}"
            : BuildUnavailableLabel(thumbnails.Message);

        TotalCacheSize =
            FormatBytes(
                Math.Max(0, analysis.SizeBytes)
                + Math.Max(0, thumbnails.SizeBytes));
    }

    private void BeginOperation(string status)
    {
        IsBusy = true;
        OperationStatus = status;
        _operationStopwatch.Restart();
        UpdateDuration();
        _durationTimer.Start();
    }

    private void EndOperation(string status)
    {
        _operationStopwatch.Stop();
        _durationTimer.Stop();
        UpdateDuration();
        OperationStatus = status;
        IsBusy = false;
    }

    private void UpdateDuration()
    {
        OperationDuration =
            "Dauer: "
            + ProcessingDurationFormatter.Format(_operationStopwatch.Elapsed);
    }

    private void NotifyCommandStates()
    {
        RefreshCacheCommand.NotifyCanExecuteChanged();
        ClearAnalysisCacheCommand.NotifyCanExecuteChanged();
        ClearThumbnailCacheCommand.NotifyCanExecuteChanged();
        ClearAllCachesCommand.NotifyCanExecuteChanged();
        RefreshStorageCommand.NotifyCanExecuteChanged();
        SelectImportLocationCommand.NotifyCanExecuteChanged();
        ClearImportLocationCommand.NotifyCanExecuteChanged();
        SelectLibraryLocationCommand.NotifyCanExecuteChanged();
        ClearLibraryLocationCommand.NotifyCanExecuteChanged();
    }

    private static string BuildUnavailableLabel(string? message)
    {
        return string.IsNullOrWhiteSpace(message)
            ? "Nicht verfügbar"
            : "Nicht verfügbar · " + message;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var value = Math.Max(0, (double)bytes);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:N0} {units[unitIndex]}"
            : $"{value:N1} {units[unitIndex]}";
    }
}
