using System.Diagnostics;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Navigation;
using Elbwald.DesktopTools.UI.Formatting;

namespace Elbwald.DesktopTools.MediaAnalyzer.ViewModels;

public sealed class MediaAnalyzerViewModel
    : ObservableObject
{
    private const int InspectorItemLimit = 500;
    private const int GalleryItemLimit = 12;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly IMediaAnalyzer _mediaAnalyzer;
    private readonly IMediaSourcePicker _sourcePicker;
    private readonly IMediaThumbnailService _thumbnailService;
    private readonly IMediaAnalysisCache _analysisCache;
    private readonly IMediaAnalysisHistoryStore _analysisHistoryStore;
    private readonly IApplicationNavigation _applicationNavigation;

    private readonly Stopwatch _analysisStopwatch = new();
    private readonly DispatcherTimer _analysisDurationTimer;

    private readonly Stopwatch _galleryStopwatch = new();
    private readonly DispatcherTimer _galleryDurationTimer;

    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _galleryCancellation;
    private bool _hasRestoredAnalysisHistory;

    private int _galleryLoadedCount;
    private int _galleryTargetCount;

    private MediaAnalysisResult? _lastResult;
    private IReadOnlyDictionary<string, IReadOnlyList<MediaAnalysisIssue>>
        _issuesByPath =
            new Dictionary<string, IReadOnlyList<MediaAnalysisIssue>>(
                PathComparer);

    private string _selectedPath = string.Empty;
    private bool _includeSubdirectories = true;
    private bool _isAnalyzing;
    private bool _isCacheBusy;
    private bool _hasResult;
    private bool _isProgressIndeterminate;
    private double _progressPercent;
    private int _selectedTabIndex;

    private string _resultHeadline =
        "Noch keine Prüfung durchgeführt.";

    private string _resultSummary =
        "Wähle einen Ordner oder eine Datei aus und starte die Prüfung.";

    private string _missingCaptureDateSummary =
        "Es fehlen keine Aufnahmedaten.";

    private bool _hasAttentionItems;

    private string _statusMessage =
        "Wähle eine Datei oder einen Ordner aus. Die Prüfung verändert keine Dateien.";

    private string _elapsedTimeText =
        "Dauer: 00:00:00";

    private string _galleryStatus =
        "Galerie wird nach der ersten Prüfung aufgebaut.";

    private string _totalFiles = "0";
    private string _totalSize = "0 B";
    private string _imageCount = "0";
    private string _problemCount = "0";
    private string _infoCount = "0";
    private string _exifCount = "0";
    private string _captureDateCount = "0";
    private string _missingCaptureDateCount = "0";
    private string _gpsCount = "0";
    private string _dimensionCount = "0";
    private string _landscapeCount = "0";
    private string _portraitCount = "0";
    private string _squareCount = "0";

    private string _cacheSummary =
        "Analyse-Cache noch nicht verwendet.";

    private string _analysisCacheDetails =
        "Noch nicht geprüft.";

    private string _thumbnailCacheDetails =
        "Noch nicht geprüft.";

    private string _analysisCacheLocation =
        string.Empty;

    private string _thumbnailCacheLocation =
        string.Empty;

    private string _cacheOperationMessage =
        "Caches enthalten ausschließlich wiederherstellbare App-Daten.";

    private string _previewFileName =
        "Noch keine Datei ausgewählt.";

    private Bitmap? _previewImage;
    private bool _hasPreview;

    private string _searchText =
        string.Empty;

    private bool _onlyProblems;
    private bool _onlyMissingCaptureDate;
    private bool _onlyRaw;

    private string _selectedSortOption =
        "Name";

    private string _inspectorSummary =
        "Noch keine Prüfung vorhanden.";

    private IReadOnlyList<BreakdownItemViewModel> _formats =
        Array.Empty<BreakdownItemViewModel>();

    private IReadOnlyList<BreakdownItemViewModel> _cameras =
        Array.Empty<BreakdownItemViewModel>();

    private IReadOnlyList<LargestFileViewModel> _largestFiles =
        Array.Empty<LargestFileViewModel>();

    private IReadOnlyList<AnalysisIssueViewModel> _issues =
        Array.Empty<AnalysisIssueViewModel>();

    private IReadOnlyList<MediaFileItemViewModel> _visibleFiles =
        Array.Empty<MediaFileItemViewModel>();

    private IReadOnlyList<MediaGalleryItemViewModel> _galleryItems =
        Array.Empty<MediaGalleryItemViewModel>();

    private MediaFileItemViewModel? _selectedFileItem;

    public MediaAnalyzerViewModel(
        IMediaAnalyzer mediaAnalyzer,
        IMediaSourcePicker sourcePicker,
        IMediaThumbnailService thumbnailService,
        IMediaAnalysisCache analysisCache,
        IMediaAnalysisHistoryStore analysisHistoryStore,
        IApplicationNavigation applicationNavigation)
    {
        ArgumentNullException.ThrowIfNull(mediaAnalyzer);
        ArgumentNullException.ThrowIfNull(sourcePicker);
        ArgumentNullException.ThrowIfNull(thumbnailService);
        ArgumentNullException.ThrowIfNull(analysisCache);
        ArgumentNullException.ThrowIfNull(analysisHistoryStore);
        ArgumentNullException.ThrowIfNull(applicationNavigation);

        _mediaAnalyzer = mediaAnalyzer;
        _sourcePicker = sourcePicker;
        _thumbnailService = thumbnailService;
        _analysisCache = analysisCache;
        _analysisHistoryStore = analysisHistoryStore;
        _applicationNavigation = applicationNavigation;

        _analysisDurationTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(1)
            };

        _analysisDurationTimer.Tick +=
            (_, _) => UpdateAnalysisDuration();

        _galleryDurationTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(1)
            };

        _galleryDurationTimer.Tick +=
            (_, _) => UpdateGalleryStatus();

        ChooseFolderCommand =
            new AsyncRelayCommand(
                ChooseFolderAsync,
                CanChooseSource);

        ChooseFileCommand =
            new AsyncRelayCommand(
                ChooseFileAsync,
                CanChooseSource);

        AnalyzeCommand =
            new AsyncRelayCommand(
                AnalyzeAsync,
                CanAnalyze);

        CancelCommand =
            new RelayCommand(
                Cancel,
                () => IsAnalyzing);

        ShowFilesCommand =
            new RelayCommand(
                () => SelectedTabIndex = 1);

        ShowHintsCommand =
            new RelayCommand(
                () => SelectedTabIndex = 3);

        PrepareImportCommand =
            new RelayCommand(
                () => _applicationNavigation.NavigateTo("import"),
                CanPrepareImport);

        RefreshCacheCommand =
            new AsyncRelayCommand(
                RefreshCacheStatusAsync,
                CanManageCache);

        ClearAnalysisCacheCommand =
            new AsyncRelayCommand(
                ClearAnalysisCacheAsync,
                CanManageCache);

        ClearThumbnailCacheCommand =
            new AsyncRelayCommand(
                ClearThumbnailCacheAsync,
                CanManageCache);
    }

    public IAsyncRelayCommand ChooseFolderCommand { get; }

    public IAsyncRelayCommand ChooseFileCommand { get; }

    public IAsyncRelayCommand AnalyzeCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ShowFilesCommand { get; }

    public IRelayCommand ShowHintsCommand { get; }

    public IRelayCommand PrepareImportCommand { get; }

    public IAsyncRelayCommand RefreshCacheCommand { get; }

    public IAsyncRelayCommand ClearAnalysisCacheCommand { get; }

    public IAsyncRelayCommand ClearThumbnailCacheCommand { get; }

    public IReadOnlyList<string> SortOptions { get; } =
        new[]
        {
            "Name",
            "Größe ↓",
            "Aufnahmedatum ↓",
            "Format",
            "Auffällige zuerst"
        };

    public string SelectedPath
    {
        get => _selectedPath;
        set
        {
            if (SetProperty(
                    ref _selectedPath,
                    value))
            {
                AnalyzeCommand.NotifyCanExecuteChanged();
                PrepareImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IncludeSubdirectories
    {
        get => _includeSubdirectories;
        set => SetProperty(
            ref _includeSubdirectories,
            value);
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (!SetProperty(
                    ref _isAnalyzing,
                    value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public bool IsCacheBusy
    {
        get => _isCacheBusy;
        private set
        {
            if (!SetProperty(
                    ref _isCacheBusy,
                    value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public bool HasResult
    {
        get => _hasResult;
        private set
        {
            if (SetProperty(
                    ref _hasResult,
                    value))
            {
                PrepareImportCommand.NotifyCanExecuteChanged();
            }
        }
    }

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

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(
            ref _selectedTabIndex,
            value);
    }

    public string ResultHeadline
    {
        get => _resultHeadline;
        private set => SetProperty(
            ref _resultHeadline,
            value);
    }

    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(
            ref _resultSummary,
            value);
    }

    public string MissingCaptureDateSummary
    {
        get => _missingCaptureDateSummary;
        private set => SetProperty(
            ref _missingCaptureDateSummary,
            value);
    }

    public bool HasAttentionItems
    {
        get => _hasAttentionItems;
        private set => SetProperty(
            ref _hasAttentionItems,
            value);
    }

    public string GalleryStatus
    {
        get => _galleryStatus;
        private set => SetProperty(
            ref _galleryStatus,
            value);
    }

    public string TotalFiles { get => _totalFiles; private set => SetProperty(ref _totalFiles, value); }

    public string TotalSize { get => _totalSize; private set => SetProperty(ref _totalSize, value); }

    public string ImageCount { get => _imageCount; private set => SetProperty(ref _imageCount, value); }

    public string ProblemCount { get => _problemCount; private set => SetProperty(ref _problemCount, value); }

    public string InfoCount { get => _infoCount; private set => SetProperty(ref _infoCount, value); }

    public string ExifCount { get => _exifCount; private set => SetProperty(ref _exifCount, value); }

    public string CaptureDateCount { get => _captureDateCount; private set => SetProperty(ref _captureDateCount, value); }

    public string MissingCaptureDateCount { get => _missingCaptureDateCount; private set => SetProperty(ref _missingCaptureDateCount, value); }

    public string GpsCount { get => _gpsCount; private set => SetProperty(ref _gpsCount, value); }

    public string DimensionCount { get => _dimensionCount; private set => SetProperty(ref _dimensionCount, value); }

    public string LandscapeCount { get => _landscapeCount; private set => SetProperty(ref _landscapeCount, value); }

    public string PortraitCount { get => _portraitCount; private set => SetProperty(ref _portraitCount, value); }

    public string SquareCount { get => _squareCount; private set => SetProperty(ref _squareCount, value); }

    public string CacheSummary
    {
        get => _cacheSummary;
        private set => SetProperty(
            ref _cacheSummary,
            value);
    }

    public string AnalysisCacheDetails
    {
        get => _analysisCacheDetails;
        private set => SetProperty(
            ref _analysisCacheDetails,
            value);
    }

    public string ThumbnailCacheDetails
    {
        get => _thumbnailCacheDetails;
        private set => SetProperty(
            ref _thumbnailCacheDetails,
            value);
    }

    public string AnalysisCacheLocation
    {
        get => _analysisCacheLocation;
        private set => SetProperty(
            ref _analysisCacheLocation,
            value);
    }

    public string ThumbnailCacheLocation
    {
        get => _thumbnailCacheLocation;
        private set => SetProperty(
            ref _thumbnailCacheLocation,
            value);
    }

    public string CacheOperationMessage
    {
        get => _cacheOperationMessage;
        private set => SetProperty(
            ref _cacheOperationMessage,
            value);
    }

    public string PreviewFileName
    {
        get => _previewFileName;
        private set => SetProperty(
            ref _previewFileName,
            value);
    }

    public Bitmap? PreviewImage
    {
        get => _previewImage;
        private set
        {
            if (ReferenceEquals(
                    _previewImage,
                    value))
            {
                return;
            }

            var previous =
                _previewImage;

            if (SetProperty(
                    ref _previewImage,
                    value))
            {
                previous?.Dispose();
                HasPreview =
                    value is not null;
            }
        }
    }

    public bool HasPreview
    {
        get => _hasPreview;
        private set => SetProperty(
            ref _hasPreview,
            value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(
                    ref _searchText,
                    value))
            {
                RefreshVisibleFiles();
            }
        }
    }

    public bool OnlyProblems
    {
        get => _onlyProblems;
        set
        {
            if (SetProperty(
                    ref _onlyProblems,
                    value))
            {
                RefreshVisibleFiles();
            }
        }
    }

    public bool OnlyMissingCaptureDate
    {
        get => _onlyMissingCaptureDate;
        set
        {
            if (SetProperty(
                    ref _onlyMissingCaptureDate,
                    value))
            {
                RefreshVisibleFiles();
            }
        }
    }

    public bool OnlyRaw
    {
        get => _onlyRaw;
        set
        {
            if (SetProperty(
                    ref _onlyRaw,
                    value))
            {
                RefreshVisibleFiles();
            }
        }
    }

    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetProperty(
                    ref _selectedSortOption,
                    value))
            {
                RefreshVisibleFiles();
            }
        }
    }

    public string InspectorSummary
    {
        get => _inspectorSummary;
        private set => SetProperty(
            ref _inspectorSummary,
            value);
    }

    public IReadOnlyList<BreakdownItemViewModel> Formats
    {
        get => _formats;
        private set => SetProperty(
            ref _formats,
            value);
    }

    public IReadOnlyList<BreakdownItemViewModel> Cameras
    {
        get => _cameras;
        private set => SetProperty(
            ref _cameras,
            value);
    }

    public IReadOnlyList<LargestFileViewModel> LargestFiles
    {
        get => _largestFiles;
        private set => SetProperty(
            ref _largestFiles,
            value);
    }

    public IReadOnlyList<AnalysisIssueViewModel> Issues
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

    public IReadOnlyList<MediaFileItemViewModel> VisibleFiles
    {
        get => _visibleFiles;
        private set => SetProperty(
            ref _visibleFiles,
            value);
    }

    public IReadOnlyList<MediaGalleryItemViewModel> GalleryItems
    {
        get => _galleryItems;
        private set => SetProperty(
            ref _galleryItems,
            value);
    }

    public MediaFileItemViewModel? SelectedFileItem
    {
        get => _selectedFileItem;
        set
        {
            if (!SetProperty(
                    ref _selectedFileItem,
                    value))
            {
                return;
            }

            OnPropertyChanged(
                nameof(HasSelectedFile));

            _ = LoadSelectedPreviewSafeAsync(
                value);
        }
    }

    public bool HasSelectedFile =>
        SelectedFileItem is not null;

    public async Task RestoreLastAnalysisAsync()
    {
        if (_hasRestoredAnalysisHistory)
        {
            return;
        }

        _hasRestoredAnalysisHistory = true;

        try
        {
            var entry =
                await _analysisHistoryStore.LoadLastAsync();

            if (entry is null
                || !string.IsNullOrWhiteSpace(SelectedPath))
            {
                return;
            }

            SelectedPath = entry.SourcePath;
            IncludeSubdirectories = entry.IncludeSubdirectories;

            var sourceAvailable =
                entry.IsFolder
                    ? Directory.Exists(entry.SourcePath)
                    : File.Exists(entry.SourcePath);

            var snapshot =
                await _analysisHistoryStore.LoadLastResultAsync();

            if (snapshot is not null
                && snapshot.CompletedUtc == entry.CompletedUtc
                && PathComparer.Equals(snapshot.RootPath, entry.SourcePath))
            {
                ApplyResult(snapshot.ToResult());

                StatusMessage = sourceAvailable
                    ? $"Letzte Prüfung: {entry.CompletedUtc.ToLocalTime():dd.MM.yyyy, HH:mm} · {entry.MediaCount:N0} Dateien · {FormatProblemCount(entry.ProblemCount)}."
                    : $"Letzte Prüfung: {entry.CompletedUtc.ToLocalTime():dd.MM.yyyy, HH:mm} · {entry.MediaCount:N0} Dateien · {FormatProblemCount(entry.ProblemCount)}. Die Quelle ist derzeit nicht erreichbar.";

                return;
            }

            StatusMessage = sourceAvailable
                ? $"Letzte Prüfung: {entry.CompletedUtc.ToLocalTime():dd.MM.yyyy, HH:mm} · {entry.MediaCount:N0} Dateien · {FormatProblemCount(entry.ProblemCount)}. Ergebnisdetails bitte einmal neu prüfen."
                : $"Letzte Prüfung: {entry.CompletedUtc.ToLocalTime():dd.MM.yyyy, HH:mm} · {entry.MediaCount:N0} Dateien · {FormatProblemCount(entry.ProblemCount)}. Die Quelle ist derzeit nicht erreichbar.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException)
        {
            StatusMessage =
                "Die letzte Prüfung konnte nicht vollständig geladen werden. Der gespeicherte Pfad bleibt erhalten; du kannst die Quelle erneut prüfen.";
        }
    }

    private bool CanChooseSource()
    {
        return !IsAnalyzing;
    }

    private bool CanAnalyze()
    {
        return !IsAnalyzing
            && !string.IsNullOrWhiteSpace(
                SelectedPath);
    }

    private bool CanManageCache()
    {
        return !IsAnalyzing
            && !IsCacheBusy;
    }

    private async Task ChooseFolderAsync()
    {
        var path =
            await _sourcePicker.PickFolderAsync();

        if (!string.IsNullOrWhiteSpace(path))
        {
            SelectedPath = path;
            StatusMessage =
                "Ordner ausgewählt. Bereit zum Prüfen.";
        }
    }

    private async Task ChooseFileAsync()
    {
        var path =
            await _sourcePicker.PickFileAsync();

        if (!string.IsNullOrWhiteSpace(path))
        {
            SelectedPath = path;
            StatusMessage =
                "Datei ausgewählt. Bereit zum Prüfen.";
        }
    }

    private async Task AnalyzeAsync()
    {
        if (!CanAnalyze())
        {
            return;
        }

        var analyzedPath = SelectedPath;
        var analyzedAsFolder = Directory.Exists(analyzedPath);
        var includeSubdirectories = IncludeSubdirectories;

        _analysisCancellation?.Dispose();
        _analysisCancellation =
            new CancellationTokenSource();

        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;

        CancelGalleryLoad();

        StartAnalysisDuration();

        IsAnalyzing = true;
        HasResult = false;
        IsProgressIndeterminate = true;
        ProgressPercent = 0;

        _lastResult = null;
        _issuesByPath =
            new Dictionary<string, IReadOnlyList<MediaAnalysisIssue>>(
                PathComparer);

        Formats =
            Array.Empty<BreakdownItemViewModel>();

        Cameras =
            Array.Empty<BreakdownItemViewModel>();

        LargestFiles =
            Array.Empty<LargestFileViewModel>();

        Issues =
            Array.Empty<AnalysisIssueViewModel>();

        VisibleFiles =
            Array.Empty<MediaFileItemViewModel>();

        ReplaceGalleryItems(
            Array.Empty<MediaGalleryItemViewModel>());

        GalleryStatus =
            "Galerie wartet auf Prüfergebnisse.";

        SelectedFileItem = null;
        PreviewImage = null;
        PreviewFileName =
            "Noch keine Datei ausgewählt.";

        CacheSummary =
            "Analyse-Cache wird geprüft …";

        InspectorSummary =
            "Prüfung läuft …";

        StatusMessage =
            "Dateien werden geprüft … es wird nichts verändert.";

        try
        {
            var progress =
                new Progress<MediaAnalysisProgress>(
                    UpdateProgress);

            var result =
                await _mediaAnalyzer.AnalyzeAsync(
                    analyzedPath,
                    new MediaAnalysisOptions
                    {
                        Recursive =
                            includeSubdirectories,
                        IncludeUnknownFiles =
                            true
                    },
                    progress,
                    _analysisCancellation.Token);

            ApplyResult(result);

            await SaveAnalysisHistorySafeAsync(
                result,
                result.RootPath,
                analyzedAsFolder,
                includeSubdirectories);

            await LoadCacheStatusCoreAsync();

            StatusMessage =
                result.ProblemCount > 0
                    ? $"Prüfung abgeschlossen · {FormatProblemCount(result.ProblemCount)} gefunden."
                    : "Prüfung abgeschlossen · keine Probleme gefunden.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage =
                "Prüfung abgebrochen. Es wurden keine Dateien verändert.";
        }
        catch (Exception exception)
        {
            StatusMessage =
                "Prüfung konnte nicht abgeschlossen werden: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            StopAnalysisDuration();

            IsAnalyzing = false;
            IsProgressIndeterminate = false;

            _analysisCancellation?.Dispose();
            _analysisCancellation = null;
        }
    }

    private void Cancel()
    {
        _analysisCancellation?.Cancel();
    }

    private void UpdateProgress(
        MediaAnalysisProgress progress)
    {
        switch (progress.Stage)
        {
            case MediaAnalysisStage.Scanning:
                IsProgressIndeterminate = true;
                ProgressPercent = 0;
                StatusMessage =
                    $"Scanne Dateisystem … {progress.FilesDiscovered:N0} Datei(en) gefunden.";
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
                    $"Lese Bildmetadaten … {progress.MetadataProcessed:N0} von {progress.MetadataTotal:N0} · "
                    + $"{progress.MetadataCacheHits:N0} Cache-Treffer.";
                break;

            case MediaAnalysisStage.Completed:
                IsProgressIndeterminate = false;
                ProgressPercent = 100;
                break;
        }
    }

    private async Task SaveAnalysisHistorySafeAsync(
        MediaAnalysisResult result,
        string sourcePath,
        bool isFolder,
        bool includeSubdirectories)
    {
        try
        {
            var completedUtc = DateTimeOffset.UtcNow;

            await _analysisHistoryStore.SaveLastAsync(
                new MediaAnalysisHistoryEntry(
                    SourcePath: sourcePath,
                    IncludeSubdirectories: includeSubdirectories,
                    IsFolder: isFolder,
                    CompletedUtc: completedUtc,
                    MediaCount: result.TotalFiles,
                    ProblemCount: result.ProblemCount));

            await _analysisHistoryStore.SaveLastResultAsync(
                MediaAnalysisResultSnapshot.FromResult(
                    result,
                    completedUtc));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or NotSupportedException
                or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(
                $"Analyseverlauf konnte nicht vollständig gespeichert werden: {exception.Message}");
        }
    }

    private bool CanPrepareImport()
    {
        return HasResult
            && !IsAnalyzing
            && _lastResult is not null
            && PathComparer.Equals(_lastResult.RootPath, SelectedPath)
            && Directory.Exists(SelectedPath);
    }

    private void UpdateFriendlyResultSummary(
        MediaAnalysisResult result)
    {
        HasAttentionItems =
            result.ProblemCount > 0
            || result.InfoCount > 0
            || result.MissingCaptureDateCount > 0;

        MissingCaptureDateSummary =
            result.MissingCaptureDateCount switch
            {
                0 => "Bei allen erkannten Bildern wurde ein Aufnahmedatum gefunden.",
                1 => "Bei 1 Bild konnte kein Aufnahmedatum gefunden werden. Das ist kein Fehler und kann später ergänzt werden.",
                _ => $"Bei {result.MissingCaptureDateCount:N0} Bildern konnte kein Aufnahmedatum gefunden werden. Das ist kein Fehler und kann später ergänzt werden."
            };

        if (result.ProblemCount > 0)
        {
            ResultHeadline =
                "Prüfung abgeschlossen – bitte Hinweise ansehen";

            ResultSummary =
                $"{result.TotalFiles:N0} Dateien wurden geprüft. "
                + $"Dabei wurden {FormatProblemCount(result.ProblemCount)} gefunden. "
                + (result.MissingCaptureDateCount > 0
                    ? MissingCaptureDateSummary
                    : "Die übrigen Dateiinformationen kannst du in Ruhe ansehen.");

            return;
        }

        if (HasAttentionItems)
        {
            ResultHeadline =
                "Prüfung abgeschlossen – Dateien sind grundsätzlich in Ordnung";

            var technicalInfo =
                result.InfoCount switch
                {
                    0 => string.Empty,
                    1 => " Bei 1 Datei konnten zusätzliche Informationen nicht vollständig gelesen werden.",
                    _ => $" Bei {result.InfoCount:N0} Dateien konnten zusätzliche Informationen nicht vollständig gelesen werden."
                };

            ResultSummary =
                $"{result.TotalFiles:N0} Dateien wurden geprüft. Es wurden keine beschädigten oder problematischen Dateien gefunden. "
                + MissingCaptureDateSummary
                + technicalInfo;

            return;
        }

        ResultHeadline =
            "Prüfung abgeschlossen – alles sieht gut aus";

        ResultSummary =
            $"{result.TotalFiles:N0} Dateien wurden geprüft. Es wurden keine Probleme oder fehlenden Aufnahmedaten gefunden.";
    }

    private static string FormatProblemCount(int problemCount)
    {
        return problemCount switch
        {
            0 => "keine Probleme",
            1 => "1 Problem",
            _ => $"{problemCount:N0} Probleme"
        };
    }

    private void ApplyResult(
        MediaAnalysisResult result)
    {
        _lastResult = result;

        _issuesByPath =
            result.Issues
                .GroupBy(
                    issue => issue.Path,
                    PathComparer)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        (IReadOnlyList<MediaAnalysisIssue>)group.ToArray(),
                    PathComparer);

        TotalFiles =
            FormatCount(
                result.TotalFiles);

        TotalSize =
            FormatBytes(
                result.TotalBytes);

        ImageCount =
            FormatCount(
                result.ImageCount);

        ProblemCount =
            FormatCount(
                result.ProblemCount);

        InfoCount =
            FormatCount(
                result.InfoCount);

        ExifCount =
            FormatCount(
                result.ImagesWithExif);

        CaptureDateCount =
            FormatCount(
                result.ImagesWithCaptureDate);

        MissingCaptureDateCount =
            FormatCount(
                result.MissingCaptureDateCount);

        GpsCount =
            FormatCount(
                result.ImagesWithGps);

        DimensionCount =
            FormatCount(
                result.ImagesWithDimensions);

        LandscapeCount =
            FormatCount(
                result.LandscapeImageCount);

        PortraitCount =
            FormatCount(
                result.PortraitImageCount);

        SquareCount =
            FormatCount(
                result.SquareImageCount);

        CacheSummary =
            result.MetadataCacheHits + result.MetadataCacheMisses == 0
                ? "Keine Bildmetadaten verarbeitet."
                : $"{result.MetadataCacheHits:N0} aus SQLite-Cache · "
                  + $"{result.MetadataCacheMisses:N0} neu gelesen";

        Formats =
            result.FormatCounts
                .Select(pair =>
                    new BreakdownItemViewModel(
                        pair.Key,
                        pair.Value))
                .ToArray();

        Cameras =
            result.CameraCounts
                .Select(pair =>
                    new BreakdownItemViewModel(
                        pair.Key,
                        pair.Value))
                .ToArray();

        LargestFiles =
            result.LargestFiles
                .Select(file =>
                    new LargestFileViewModel(file))
                .ToArray();

        Issues =
            result.Issues
                .OrderByDescending(issue =>
                    issue.Severity)
                .ThenBy(
                    issue => issue.Path,
                    StringComparer.CurrentCultureIgnoreCase)
                .Take(100)
                .Select(issue =>
                    new AnalysisIssueViewModel(issue))
                .ToArray();

        UpdateFriendlyResultSummary(result);
        SelectedTabIndex = 0;
        HasResult = true;

        RefreshVisibleFiles();
    }

    private void RefreshVisibleFiles()
    {
        if (_lastResult is null)
        {
            VisibleFiles =
                Array.Empty<MediaFileItemViewModel>();

            InspectorSummary =
                "Noch keine Prüfung vorhanden.";

            SelectedFileItem = null;

            CancelGalleryLoad();

            ReplaceGalleryItems(
                Array.Empty<MediaGalleryItemViewModel>());

            GalleryStatus =
                "Galerie nicht verfügbar.";

            return;
        }

        var previousPath =
            SelectedFileItem?.FullPath;

        IEnumerable<MediaAnalyzedFile> query =
            _lastResult.Files;

        var search =
            SearchText.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query =
                query.Where(file =>
                    MatchesSearch(
                        file,
                        search));
        }

        if (OnlyProblems)
        {
            query =
                query.Where(file =>
                    HasProblem(
                        file.File.FullPath));
        }

        if (OnlyMissingCaptureDate)
        {
            query =
                query.Where(file =>
                    file.File.MediaType == MediaFileType.Image
                    && file.ImageMetadata?.HasCaptureDate != true);
        }

        if (OnlyRaw)
        {
            query =
                query.Where(file =>
                    IsRawExtension(
                        file.File.Extension));
        }

        query =
            ApplySort(
                query);

        var matches =
            query.ToArray();

        var visible =
            matches
                .Take(
                    InspectorItemLimit)
                .Select(file =>
                    new MediaFileItemViewModel(
                        file,
                        GetIssues(
                            file.File.FullPath)))
                .ToArray();

        VisibleFiles = visible;

        InspectorSummary =
            matches.Length > InspectorItemLimit
                ? $"{InspectorItemLimit:N0} von {matches.Length:N0} Treffern angezeigt · Filter weiter eingrenzen für mehr Übersicht."
                : $"{matches.Length:N0} Treffer.";

        MediaFileItemViewModel? nextSelection = null;

        if (!string.IsNullOrWhiteSpace(previousPath))
        {
            nextSelection =
                visible.FirstOrDefault(item =>
                    PathComparer.Equals(
                        item.FullPath,
                        previousPath));
        }

        nextSelection ??=
            visible.FirstOrDefault(item =>
                item.IsImage);

        nextSelection ??=
            visible.FirstOrDefault();

        SelectedFileItem =
            nextSelection;

        StartGalleryLoad(
            visible);
    }

    private IEnumerable<MediaAnalyzedFile> ApplySort(
        IEnumerable<MediaAnalyzedFile> source)
    {
        return SelectedSortOption switch
        {
            "Größe ↓" =>
                source
                    .OrderByDescending(file =>
                        file.File.Length)
                    .ThenBy(file =>
                        file.File.FileName,
                        StringComparer.CurrentCultureIgnoreCase),

            "Aufnahmedatum ↓" =>
                source
                    .OrderByDescending(file =>
                        file.ImageMetadata?.CapturedAt
                        ?? DateTime.MinValue)
                    .ThenBy(file =>
                        file.File.FileName,
                        StringComparer.CurrentCultureIgnoreCase),

            "Format" =>
                source
                    .OrderBy(file =>
                        file.File.Extension,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(file =>
                        file.File.FileName,
                        StringComparer.CurrentCultureIgnoreCase),

            "Auffällige zuerst" =>
                source
                    .OrderByDescending(file =>
                        GetSeverityRank(
                            file.File.FullPath))
                    .ThenBy(file =>
                        file.File.FileName,
                        StringComparer.CurrentCultureIgnoreCase),

            _ =>
                source
                    .OrderBy(file =>
                        file.File.FileName,
                        StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private bool MatchesSearch(
        MediaAnalyzedFile file,
        string search)
    {
        if (file.File.FileName.Contains(
                search,
                StringComparison.CurrentCultureIgnoreCase)
            || file.File.FullPath.Contains(
                search,
                StringComparison.CurrentCultureIgnoreCase)
            || file.File.Extension.Contains(
                search,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        var make =
            file.ImageMetadata?.CameraMake;

        var model =
            file.ImageMetadata?.CameraModel;

        return (!string.IsNullOrWhiteSpace(make)
                && make.Contains(
                    search,
                    StringComparison.CurrentCultureIgnoreCase))
               || (!string.IsNullOrWhiteSpace(model)
                   && model.Contains(
                       search,
                       StringComparison.CurrentCultureIgnoreCase));
    }

    private bool HasProblem(
        string path)
    {
        return GetIssues(path)
            .Any(issue =>
                issue.Severity
                == MediaAnalysisSeverity.Problem);
    }

    private int GetSeverityRank(
        string path)
    {
        var issues =
            GetIssues(path);

        if (issues.Any(issue =>
                issue.Severity == MediaAnalysisSeverity.Problem))
        {
            return 3;
        }

        if (issues.Any(issue =>
                issue.Severity == MediaAnalysisSeverity.Warning))
        {
            return 2;
        }

        return issues.Any(issue =>
            issue.Severity == MediaAnalysisSeverity.Info)
            ? 1
            : 0;
    }

    private IReadOnlyList<MediaAnalysisIssue> GetIssues(
        string path)
    {
        return _issuesByPath.TryGetValue(
            path,
            out var issues)
            ? issues
            : Array.Empty<MediaAnalysisIssue>();
    }

    private async Task LoadSelectedPreviewSafeAsync(
        MediaFileItemViewModel? item)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();

        _previewCancellation =
            new CancellationTokenSource();

        var cancellationToken =
            _previewCancellation.Token;

        if (item is null)
        {
            PreviewImage = null;
            PreviewFileName =
                "Keine Datei ausgewählt.";
            return;
        }

        PreviewImage = null;
        PreviewFileName =
            item.IsImage
                ? $"{item.FileName} · Vorschau wird geladen …"
                : $"{item.FileName} · keine Bildvorschau";

        if (!item.IsImage)
        {
            return;
        }

        try
        {
            var thumbnail =
                await _thumbnailService.GetThumbnailAsync(
                    item.AnalyzedFile.File,
                    width: 720,
                    cancellationToken:
                        cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (!thumbnail.IsSuccessful
                || thumbnail.EncodedImage is null)
            {
                PreviewFileName =
                    $"{item.FileName} · Vorschau nicht verfügbar";
                return;
            }

            using var stream =
                new MemoryStream(
                    thumbnail.EncodedImage,
                    writable: false);

            PreviewImage =
                new Bitmap(stream);

            PreviewFileName =
                item.FileName
                + (thumbnail.IsFromCache
                    ? " · Thumbnail aus Cache"
                    : " · Thumbnail neu erzeugt");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or InvalidOperationException
                or NotSupportedException)
        {
            PreviewImage = null;
            PreviewFileName =
                $"{item.FileName} · Vorschau nicht verfügbar";
        }
    }

    private void StartAnalysisDuration()
    {
        _analysisStopwatch.Restart();

        UpdateAnalysisDuration();

        _analysisDurationTimer.Start();
    }

    private void StopAnalysisDuration()
    {
        _analysisStopwatch.Stop();

        _analysisDurationTimer.Stop();

        UpdateAnalysisDuration();
    }

    private void UpdateAnalysisDuration()
    {
        ElapsedTimeText =
            "Dauer: "
            + ProcessingDurationFormatter.Format(
                _analysisStopwatch.Elapsed);
    }

    private void StartGalleryLoad(
        IReadOnlyList<MediaFileItemViewModel> visibleFiles)
    {
        CancelGalleryLoad();

        var galleryItems =
            visibleFiles
                .Where(item =>
                    item.IsImage)
                .Take(
                    GalleryItemLimit)
                .Select(item =>
                    new MediaGalleryItemViewModel(
                        item,
                        selected =>
                            SelectedFileItem = selected))
                .ToArray();

        ReplaceGalleryItems(
            galleryItems);

        if (galleryItems.Length == 0)
        {
            GalleryStatus =
                "Keine Bilder für die Vorschaugalerie.";

            return;
        }

        _galleryCancellation =
            new CancellationTokenSource();

        _galleryLoadedCount = 0;
        _galleryTargetCount =
            galleryItems.Length;

        _galleryStopwatch.Restart();

        UpdateGalleryStatus();

        _galleryDurationTimer.Start();

        _ = LoadGallerySafeAsync(
            galleryItems,
            _galleryCancellation.Token);
    }

    private async Task LoadGallerySafeAsync(
        IReadOnlyList<MediaGalleryItemViewModel> galleryItems,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var item in galleryItems)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var thumbnail =
                    await _thumbnailService.GetThumbnailAsync(
                        item.FileItem.AnalyzedFile.File,
                        width: 240,
                        cancellationToken:
                            cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (thumbnail.IsSuccessful
                    && thumbnail.EncodedImage is not null)
                {
                    try
                    {
                        using var stream =
                            new MemoryStream(
                                thumbnail.EncodedImage,
                                writable: false);

                        item.SetThumbnail(
                            new Bitmap(stream));
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException
                            or IOException
                            or InvalidOperationException
                            or NotSupportedException)
                    {
                        item.SetThumbnail(
                            null);
                    }
                }

                _galleryLoadedCount++;

                UpdateGalleryStatus();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _galleryStopwatch.Stop();

                _galleryDurationTimer.Stop();

                UpdateGalleryStatus();
            }
        }
    }

    private void UpdateGalleryStatus()
    {
        GalleryStatus =
            $"{_galleryLoadedCount:N0} von {_galleryTargetCount:N0} Vorschauen"
            + $" · Dauer {ProcessingDurationFormatter.Format(_galleryStopwatch.Elapsed)}";
    }

    private void CancelGalleryLoad()
    {
        _galleryCancellation?.Cancel();
        _galleryCancellation?.Dispose();
        _galleryCancellation = null;

        _galleryDurationTimer.Stop();

        _galleryStopwatch.Stop();
    }

    private void ReplaceGalleryItems(
        IReadOnlyList<MediaGalleryItemViewModel> items)
    {
        foreach (var existing in GalleryItems)
        {
            existing.Dispose();
        }

        GalleryItems =
            items;
    }

    private async Task RefreshCacheStatusAsync()
    {
        if (!CanManageCache())
        {
            return;
        }

        var stopwatch =
            Stopwatch.StartNew();

        IsCacheBusy = true;

        try
        {
            await LoadCacheStatusCoreAsync();

            CacheOperationMessage +=
                $" · Dauer {ProcessingDurationFormatter.Format(stopwatch.Elapsed, includeTenths: true)}";
        }
        finally
        {
            IsCacheBusy = false;
        }
    }

    private async Task LoadCacheStatusCoreAsync()
    {
        try
        {
            var analysis =
                await _analysisCache.GetStatisticsAsync();

            var thumbnails =
                await _thumbnailService.GetCacheStatisticsAsync();

            AnalysisCacheLocation =
                analysis.Location;

            ThumbnailCacheLocation =
                thumbnails.Location;

            AnalysisCacheDetails =
                analysis.IsAvailable
                    ? $"{analysis.EntryCount:N0} Metadatensätze · {FormatBytes(analysis.SizeBytes)}"
                    : "Nicht verfügbar"
                      + (string.IsNullOrWhiteSpace(analysis.Message)
                          ? string.Empty
                          : $" · {analysis.Message}");

            ThumbnailCacheDetails =
                thumbnails.IsAvailable
                    ? $"{thumbnails.EntryCount:N0} Thumbnails · {FormatBytes(thumbnails.SizeBytes)}"
                    : "Nicht verfügbar"
                      + (string.IsNullOrWhiteSpace(thumbnails.Message)
                          ? string.Empty
                          : $" · {thumbnails.Message}");

            CacheOperationMessage =
                "Cache-Status aktualisiert. Beide Caches sind vollständig wiederherstellbar.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            CacheOperationMessage =
                "Cache-Status konnte nicht vollständig gelesen werden: "
                + exception.Message;
        }
    }

    private async Task ClearAnalysisCacheAsync()
    {
        if (!CanManageCache())
        {
            return;
        }

        var stopwatch =
            Stopwatch.StartNew();

        IsCacheBusy = true;

        try
        {
            await _analysisCache.ClearAsync();

            CacheSummary =
                "SQLite-Analyse-Cache geleert. Beim nächsten Scan werden Metadaten neu gelesen.";

            CacheOperationMessage =
                "Analyse-Cache wurde geleert. Originalmedien waren davon nicht betroffen."
                + $" · Dauer {ProcessingDurationFormatter.Format(stopwatch.Elapsed, includeTenths: true)}";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            CacheOperationMessage =
                "Analyse-Cache konnte nicht geleert werden: "
                + exception.Message
                + $" · Dauer {ProcessingDurationFormatter.Format(stopwatch.Elapsed, includeTenths: true)}";
        }
        finally
        {
            IsCacheBusy = false;
        }

        var operationMessage =
            CacheOperationMessage;

        await LoadCacheStatusCoreAsync();

        CacheOperationMessage =
            operationMessage;
    }

    private async Task ClearThumbnailCacheAsync()
    {
        if (!CanManageCache())
        {
            return;
        }

        var stopwatch =
            Stopwatch.StartNew();

        IsCacheBusy = true;

        try
        {
            await _thumbnailService.ClearCacheAsync();

            PreviewImage = null;

            CacheOperationMessage =
                "Thumbnail-Cache wurde geleert. Originalmedien waren davon nicht betroffen."
                + $" · Dauer {ProcessingDurationFormatter.Format(stopwatch.Elapsed, includeTenths: true)}";

            if (SelectedFileItem is not null)
            {
                _ = LoadSelectedPreviewSafeAsync(
                    SelectedFileItem);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            CacheOperationMessage =
                "Thumbnail-Cache konnte nicht geleert werden: "
                + exception.Message
                + $" · Dauer {ProcessingDurationFormatter.Format(stopwatch.Elapsed, includeTenths: true)}";
        }
        finally
        {
            IsCacheBusy = false;
        }

        var operationMessage =
            CacheOperationMessage;

        await LoadCacheStatusCoreAsync();

        CacheOperationMessage =
            operationMessage;
    }

    private void NotifyCommandStates()
    {
        AnalyzeCommand.NotifyCanExecuteChanged();
        ChooseFolderCommand.NotifyCanExecuteChanged();
        ChooseFileCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RefreshCacheCommand.NotifyCanExecuteChanged();
        ClearAnalysisCacheCommand.NotifyCanExecuteChanged();
        ClearThumbnailCacheCommand.NotifyCanExecuteChanged();
        PrepareImportCommand.NotifyCanExecuteChanged();
    }

    private static bool IsRawExtension(
        string extension)
    {
        return extension.Equals(".arw", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".dng", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".cr2", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".cr3", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".nef", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".raf", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".orf", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".rw2", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".pef", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCount(
        int value)
    {
        return value.ToString(
            "N0",
            CultureInfo.CurrentCulture);
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
