using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.UI.Formatting;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class SettingsViewModel
    : ObservableObject
{
    private readonly IMediaAnalysisCache _analysisCache;
    private readonly IMediaThumbnailService _thumbnailService;

    private readonly Stopwatch _operationStopwatch = new();
    private readonly DispatcherTimer _durationTimer;

    private bool _isBusy;

    private string _operationStatus =
        "Bereit.";

    private string _operationDuration =
        "Dauer: 00:00:00";

    private string _analysisCacheSummary =
        "Wird gelesen …";

    private string _analysisCacheLocation =
        string.Empty;

    private string _thumbnailCacheSummary =
        "Wird gelesen …";

    private string _thumbnailCacheLocation =
        string.Empty;

    private string _totalCacheSize =
        "0 B";

    public SettingsViewModel(
        IMediaAnalysisCache analysisCache,
        IMediaThumbnailService thumbnailService)
    {
        ArgumentNullException.ThrowIfNull(analysisCache);
        ArgumentNullException.ThrowIfNull(thumbnailService);

        _analysisCache = analysisCache;
        _thumbnailService = thumbnailService;

        _durationTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(1)
            };

        _durationTimer.Tick +=
            (_, _) => UpdateDuration();

        RefreshCacheCommand =
            new AsyncRelayCommand(
                RefreshCacheAsync,
                CanManageCache);

        ClearAnalysisCacheCommand =
            new AsyncRelayCommand(
                ClearAnalysisCacheAsync,
                CanManageCache);

        ClearThumbnailCacheCommand =
            new AsyncRelayCommand(
                ClearThumbnailCacheAsync,
                CanManageCache);

        ClearAllCachesCommand =
            new AsyncRelayCommand(
                ClearAllCachesAsync,
                CanManageCache);

        _ = RefreshCacheAsync();
    }

    public IAsyncRelayCommand RefreshCacheCommand { get; }

    public IAsyncRelayCommand ClearAnalysisCacheCommand { get; }

    public IAsyncRelayCommand ClearThumbnailCacheCommand { get; }

    public IAsyncRelayCommand ClearAllCachesCommand { get; }

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

            NotifyCommandStates();
        }
    }

    public string OperationStatus
    {
        get => _operationStatus;
        private set => SetProperty(
            ref _operationStatus,
            value);
    }

    public string OperationDuration
    {
        get => _operationDuration;
        private set => SetProperty(
            ref _operationDuration,
            value);
    }

    public string AnalysisCacheSummary
    {
        get => _analysisCacheSummary;
        private set => SetProperty(
            ref _analysisCacheSummary,
            value);
    }

    public string AnalysisCacheLocation
    {
        get => _analysisCacheLocation;
        private set => SetProperty(
            ref _analysisCacheLocation,
            value);
    }

    public string ThumbnailCacheSummary
    {
        get => _thumbnailCacheSummary;
        private set => SetProperty(
            ref _thumbnailCacheSummary,
            value);
    }

    public string ThumbnailCacheLocation
    {
        get => _thumbnailCacheLocation;
        private set => SetProperty(
            ref _thumbnailCacheLocation,
            value);
    }

    public string TotalCacheSize
    {
        get => _totalCacheSize;
        private set => SetProperty(
            ref _totalCacheSize,
            value);
    }

    public string PerformanceProfile =>
        "Sicheres Standardprofil";

    public string PerformanceDetails =>
        "Read-only Analyse · SQLite-Metadaten-Cache · lazy Thumbnails · "
        + "max. 500 Inspector-Treffer · max. 12 Galerie-Vorschauen";

    public string DataPolicy =>
        "Cache-Dateien sind vollständig wiederherstellbar und dürfen jederzeit "
        + "neu aufgebaut werden. Originalmedien und Recovery-Daten sind keine Cache-Daten.";

    private bool CanManageCache()
    {
        return !IsBusy;
    }

    private async Task RefreshCacheAsync()
    {
        if (!CanManageCache())
        {
            return;
        }

        BeginOperation(
            "Cache-Status wird gelesen …");

        try
        {
            await LoadCacheStatisticsAsync();

            EndOperation(
                "Cache-Status aktualisiert.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            EndOperation(
                "Cache-Status konnte nicht vollständig gelesen werden: "
                + exception.Message);
        }
    }

    private async Task ClearAnalysisCacheAsync()
    {
        if (!CanManageCache())
        {
            return;
        }

        BeginOperation(
            "SQLite-Analyse-Cache wird geleert …");

        try
        {
            await _analysisCache.ClearAsync();

            await LoadCacheStatisticsAsync();

            EndOperation(
                "Analyse-Cache geleert. Originalmedien wurden nicht verändert.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            EndOperation(
                "Analyse-Cache konnte nicht geleert werden: "
                + exception.Message);
        }
    }

    private async Task ClearThumbnailCacheAsync()
    {
        if (!CanManageCache())
        {
            return;
        }

        BeginOperation(
            "Thumbnail-Cache wird geleert …");

        try
        {
            await _thumbnailService.ClearCacheAsync();

            await LoadCacheStatisticsAsync();

            EndOperation(
                "Thumbnail-Cache geleert. Originalmedien wurden nicht verändert.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            EndOperation(
                "Thumbnail-Cache konnte nicht geleert werden: "
                + exception.Message);
        }
    }

    private async Task ClearAllCachesAsync()
    {
        if (!CanManageCache())
        {
            return;
        }

        BeginOperation(
            "Alle Medien-Caches werden geleert …");

        try
        {
            await _analysisCache.ClearAsync();

            await _thumbnailService.ClearCacheAsync();

            await LoadCacheStatisticsAsync();

            EndOperation(
                "Alle Medien-Caches geleert. Originalmedien und Recovery-Daten blieben unangetastet.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            EndOperation(
                "Die Caches konnten nicht vollständig geleert werden: "
                + exception.Message);
        }
    }

    private async Task LoadCacheStatisticsAsync()
    {
        var analysis =
            await _analysisCache.GetStatisticsAsync();

        var thumbnails =
            await _thumbnailService.GetCacheStatisticsAsync();

        AnalysisCacheLocation =
            analysis.Location;

        ThumbnailCacheLocation =
            thumbnails.Location;

        AnalysisCacheSummary =
            analysis.IsAvailable
                ? $"{analysis.EntryCount:N0} Metadatensätze · {FormatBytes(analysis.SizeBytes)}"
                : BuildUnavailableLabel(
                    analysis.Message);

        ThumbnailCacheSummary =
            thumbnails.IsAvailable
                ? $"{thumbnails.EntryCount:N0} Thumbnails · {FormatBytes(thumbnails.SizeBytes)}"
                : BuildUnavailableLabel(
                    thumbnails.Message);

        TotalCacheSize =
            FormatBytes(
                Math.Max(
                    0,
                    analysis.SizeBytes)
                + Math.Max(
                    0,
                    thumbnails.SizeBytes));
    }

    private void BeginOperation(
        string status)
    {
        IsBusy = true;

        OperationStatus =
            status;

        _operationStopwatch.Restart();

        UpdateDuration();

        _durationTimer.Start();
    }

    private void EndOperation(
        string status)
    {
        _operationStopwatch.Stop();

        _durationTimer.Stop();

        UpdateDuration();

        OperationStatus =
            status;

        IsBusy = false;
    }

    private void UpdateDuration()
    {
        OperationDuration =
            "Dauer: "
            + ProcessingDurationFormatter.Format(
                _operationStopwatch.Elapsed);
    }

    private void NotifyCommandStates()
    {
        RefreshCacheCommand.NotifyCanExecuteChanged();
        ClearAnalysisCacheCommand.NotifyCanExecuteChanged();
        ClearThumbnailCacheCommand.NotifyCanExecuteChanged();
        ClearAllCachesCommand.NotifyCanExecuteChanged();
    }

    private static string BuildUnavailableLabel(
        string? message)
    {
        return string.IsNullOrWhiteSpace(message)
            ? "Nicht verfügbar"
            : "Nicht verfügbar · " + message;
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
