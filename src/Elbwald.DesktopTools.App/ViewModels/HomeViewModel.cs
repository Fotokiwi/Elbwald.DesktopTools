using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    private readonly IStartupRecoveryService _startupRecoveryService;
    private readonly IFileOperationRecoveryCoordinator _recoveryCoordinator;
    private readonly IOperationJournalMaintenance _journalMaintenance;
    private readonly IMediaIndexService _mediaIndexService;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageLocationResolver;
    private readonly IMediaThumbnailService _thumbnailService;
    private readonly MediaThumbnailOptions _thumbnailOptions;
    private readonly IMediaAnalysisHistoryStore _analysisHistoryStore;
    private readonly IMediaImportHistoryStore _importHistoryStore;

    private bool _isDashboardBusy;
    private string _galleryMediaCountText = "0";
    private string _galleryFolderCountText = "0";
    private string _galleryLastUpdatedText = "Noch nicht indexiert";
    private string _analyzeHintText = "Wähle einen neuen Ordner für eine read-only Prüfung.";
    private string _lastAnalysisPathText = "Noch kein Prüfpfad gespeichert";
    private string _lastAnalysisAgeText = "–";
    private string _lastAnalysisSummaryText = "Noch keine Analyse gespeichert";
    private string _lastImportDestinationText = "Noch kein Import durchgeführt";
    private string _lastImportAgeText = "–";
    private string _lastImportSummaryText = "Importverlauf ist noch leer";
    private string _organizeHintText = "Ordner und Sammlungen erscheinen hier automatisch.";
    private string _latestMediaTitle = "Noch keine Medien";
    private string _latestMediaSubtitle = "Importiere deine ersten Medien";
    private string _latestMediaAgeText = string.Empty;
    private string _latestFolderTitle = "Noch kein Import";
    private string _latestFolderSubtitle = "Der letzte Importordner erscheint hier";
    private string _latestFolderAgeText = string.Empty;
    private string _latestLibraryTitle = "Hauptbibliothek";
    private string _latestLibrarySubtitle = "Noch keine Medien im Index";
    private string _latestLibraryAgeText = string.Empty;

    private Bitmap? _galleryPreviewPrimary;
    private Bitmap? _galleryPreviewSecondary1;
    private Bitmap? _galleryPreviewSecondary2;
    private Bitmap? _galleryPreviewSecondary3;
    private Bitmap? _galleryPreviewSecondary4;

    private string _recoveryStatusTitle = string.Empty;
    private string _recoveryStatusMessage = string.Empty;
    private string _recoveryActionMessage = string.Empty;

    private bool _isRecoveryClean;
    private bool _isRecoveryAttentionRequired;
    private bool _isRecoveryScanFailed;
    private bool _isProcessLockUnavailable;
    private bool _isRecoveryNotScanned;
    private bool _canStartFileOperations;
    private bool _hasRecoveryCandidates;
    private bool _hasJournalCorruption;
    private bool _isRecoveryBusy;
    private bool _isRecoveryPanelVisible;
    private bool _isRecoveryActionMessageVisible;
    private bool _canRunAutomaticRecovery;

    private IReadOnlyList<RecoveryCandidateViewModel> _recoveryCandidates =
        Array.Empty<RecoveryCandidateViewModel>();

    private IReadOnlyList<RecoveryResultViewModel> _recoveryResults =
        Array.Empty<RecoveryResultViewModel>();

    public HomeViewModel(
        IModuleRegistry moduleRegistry,
        IModuleLoadReport moduleLoadReport,
        INavigationService navigationService,
        IStartupRecoveryService startupRecoveryService,
        IFileOperationRecoveryCoordinator recoveryCoordinator,
        IOperationJournalMaintenance journalMaintenance,
        IMediaIndexService mediaIndexService,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver,
        IMediaThumbnailService thumbnailService,
        MediaThumbnailOptions thumbnailOptions,
        IMediaAnalysisHistoryStore analysisHistoryStore,
        IMediaImportHistoryStore importHistoryStore)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(moduleLoadReport);
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(startupRecoveryService);
        ArgumentNullException.ThrowIfNull(recoveryCoordinator);
        ArgumentNullException.ThrowIfNull(journalMaintenance);
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        ArgumentNullException.ThrowIfNull(storageSettingsStore);
        ArgumentNullException.ThrowIfNull(storageLocationResolver);
        ArgumentNullException.ThrowIfNull(thumbnailService);
        ArgumentNullException.ThrowIfNull(thumbnailOptions);
        ArgumentNullException.ThrowIfNull(analysisHistoryStore);
        ArgumentNullException.ThrowIfNull(importHistoryStore);

        _startupRecoveryService = startupRecoveryService;
        _recoveryCoordinator = recoveryCoordinator;
        _journalMaintenance = journalMaintenance;
        _mediaIndexService = mediaIndexService;
        _storageSettingsStore = storageSettingsStore;
        _storageLocationResolver = storageLocationResolver;
        _thumbnailService = thumbnailService;
        _thumbnailOptions = thumbnailOptions;
        _analysisHistoryStore = analysisHistoryStore;
        _importHistoryStore = importHistoryStore;

        Modules = moduleRegistry.Modules
            .Select(module => new HomeModuleItemViewModel(
                module.GetDescriptor(),
                navigationService))
            .OrderBy(module => module.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        ModuleSummary = Modules.Count switch
        {
            0 => "Noch keine Werkzeuge geladen.",
            1 => "1 Werkzeug ist bereit.",
            _ => $"{Modules.Count} Werkzeuge sind bereit."
        };

        FailedModules = moduleLoadReport.Failures
            .Select(result => new ModuleLoadFailureViewModel(result))
            .ToArray();

        OpenAnalyzeCommand = new RelayCommand(
            () => navigationService.NavigateTo("analyze"));

        OpenOrganizeCommand = new RelayCommand(
            () => navigationService.NavigateTo("organize"));

        OpenEditCommand = new RelayCommand(
            () => navigationService.NavigateTo("edit"));

        OpenBackupCommand = new RelayCommand(
            () => navigationService.NavigateTo("backup"));

        OpenLibraryCommand = new RelayCommand(
            () => navigationService.NavigateTo("library"));

        OpenImportCommand = new RelayCommand(
            () => navigationService.NavigateTo("import"));

        OpenMetadataCommand = new RelayCommand(
            () => navigationService.NavigateTo("metadata"));

        OpenLibraryHealthCommand = new RelayCommand(
            () => navigationService.NavigateTo("library-health"));

        RunSafeRecoveryCommand = new AsyncRelayCommand(
            RunSafeRecoveryAsync,
            CanRunSafeRecovery);

        RefreshRecoveryCommand = new AsyncRelayCommand(
            RefreshRecoveryAsync,
            CanRefreshRecovery);

        RepairJournalCommand = new AsyncRelayCommand(
            RepairJournalAsync,
            CanRepairJournal);

        ApplyRecoverySnapshot(
            startupRecoveryService.Current);
    }

    public string Title => "Willkommen";

    public string ModuleSummary { get; }

    public IReadOnlyList<HomeModuleItemViewModel> Modules { get; }

    public IReadOnlyList<ModuleLoadFailureViewModel> FailedModules { get; }

    public bool HasModuleLoadFailures => FailedModules.Count > 0;

    public IRelayCommand OpenAnalyzeCommand { get; }

    public IRelayCommand OpenOrganizeCommand { get; }

    public IRelayCommand OpenEditCommand { get; }

    public IRelayCommand OpenBackupCommand { get; }

    public IRelayCommand OpenLibraryCommand { get; }

    public IRelayCommand OpenImportCommand { get; }

    public IRelayCommand OpenMetadataCommand { get; }

    public IRelayCommand OpenLibraryHealthCommand { get; }

    public IAsyncRelayCommand RunSafeRecoveryCommand { get; }

    public IAsyncRelayCommand RefreshRecoveryCommand { get; }

    public IAsyncRelayCommand RepairJournalCommand { get; }

    public bool IsDashboardBusy
    {
        get => _isDashboardBusy;
        private set => SetProperty(ref _isDashboardBusy, value);
    }

    public string GalleryMediaCountText
    {
        get => _galleryMediaCountText;
        private set => SetProperty(ref _galleryMediaCountText, value);
    }

    public string GalleryFolderCountText
    {
        get => _galleryFolderCountText;
        private set => SetProperty(ref _galleryFolderCountText, value);
    }

    public string GalleryLastUpdatedText
    {
        get => _galleryLastUpdatedText;
        private set => SetProperty(ref _galleryLastUpdatedText, value);
    }

    public string AnalyzeHintText
    {
        get => _analyzeHintText;
        private set => SetProperty(ref _analyzeHintText, value);
    }

    public string LastAnalysisPathText
    {
        get => _lastAnalysisPathText;
        private set => SetProperty(ref _lastAnalysisPathText, value);
    }

    public string LastAnalysisAgeText
    {
        get => _lastAnalysisAgeText;
        private set => SetProperty(ref _lastAnalysisAgeText, value);
    }

    public string LastAnalysisSummaryText
    {
        get => _lastAnalysisSummaryText;
        private set => SetProperty(ref _lastAnalysisSummaryText, value);
    }

    public string LastImportDestinationText
    {
        get => _lastImportDestinationText;
        private set => SetProperty(ref _lastImportDestinationText, value);
    }

    public string LastImportAgeText
    {
        get => _lastImportAgeText;
        private set => SetProperty(ref _lastImportAgeText, value);
    }

    public string LastImportSummaryText
    {
        get => _lastImportSummaryText;
        private set => SetProperty(ref _lastImportSummaryText, value);
    }

    public string OrganizeHintText
    {
        get => _organizeHintText;
        private set => SetProperty(ref _organizeHintText, value);
    }

    public string LatestMediaTitle
    {
        get => _latestMediaTitle;
        private set => SetProperty(ref _latestMediaTitle, value);
    }

    public string LatestMediaSubtitle
    {
        get => _latestMediaSubtitle;
        private set => SetProperty(ref _latestMediaSubtitle, value);
    }

    public string LatestMediaAgeText
    {
        get => _latestMediaAgeText;
        private set => SetProperty(ref _latestMediaAgeText, value);
    }

    public string LatestFolderTitle
    {
        get => _latestFolderTitle;
        private set => SetProperty(ref _latestFolderTitle, value);
    }

    public string LatestFolderSubtitle
    {
        get => _latestFolderSubtitle;
        private set => SetProperty(ref _latestFolderSubtitle, value);
    }

    public string LatestFolderAgeText
    {
        get => _latestFolderAgeText;
        private set => SetProperty(ref _latestFolderAgeText, value);
    }

    public string LatestLibraryTitle
    {
        get => _latestLibraryTitle;
        private set => SetProperty(ref _latestLibraryTitle, value);
    }

    public string LatestLibrarySubtitle
    {
        get => _latestLibrarySubtitle;
        private set => SetProperty(ref _latestLibrarySubtitle, value);
    }

    public string LatestLibraryAgeText
    {
        get => _latestLibraryAgeText;
        private set => SetProperty(ref _latestLibraryAgeText, value);
    }

    public Bitmap? GalleryPreviewPrimary
    {
        get => _galleryPreviewPrimary;
        private set => ReplacePreview(ref _galleryPreviewPrimary, value, nameof(GalleryPreviewPrimary));
    }

    public Bitmap? GalleryPreviewSecondary1
    {
        get => _galleryPreviewSecondary1;
        private set => ReplacePreview(ref _galleryPreviewSecondary1, value, nameof(GalleryPreviewSecondary1));
    }

    public Bitmap? GalleryPreviewSecondary2
    {
        get => _galleryPreviewSecondary2;
        private set => ReplacePreview(ref _galleryPreviewSecondary2, value, nameof(GalleryPreviewSecondary2));
    }

    public Bitmap? GalleryPreviewSecondary3
    {
        get => _galleryPreviewSecondary3;
        private set => ReplacePreview(ref _galleryPreviewSecondary3, value, nameof(GalleryPreviewSecondary3));
    }

    public Bitmap? GalleryPreviewSecondary4
    {
        get => _galleryPreviewSecondary4;
        private set => ReplacePreview(ref _galleryPreviewSecondary4, value, nameof(GalleryPreviewSecondary4));
    }

    public string RecoveryStatusTitle
    {
        get => _recoveryStatusTitle;
        private set => SetProperty(
            ref _recoveryStatusTitle,
            value);
    }

    public string RecoveryStatusMessage
    {
        get => _recoveryStatusMessage;
        private set => SetProperty(
            ref _recoveryStatusMessage,
            value);
    }

    public string RecoveryActionMessage
    {
        get => _recoveryActionMessage;
        private set
        {
            if (SetProperty(
                    ref _recoveryActionMessage,
                    value))
            {
                IsRecoveryActionMessageVisible =
                    !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    public bool IsRecoveryClean
    {
        get => _isRecoveryClean;
        private set => SetProperty(
            ref _isRecoveryClean,
            value);
    }

    public bool IsRecoveryAttentionRequired
    {
        get => _isRecoveryAttentionRequired;
        private set => SetProperty(
            ref _isRecoveryAttentionRequired,
            value);
    }

    public bool IsRecoveryScanFailed
    {
        get => _isRecoveryScanFailed;
        private set => SetProperty(
            ref _isRecoveryScanFailed,
            value);
    }

    public bool IsProcessLockUnavailable
    {
        get => _isProcessLockUnavailable;
        private set => SetProperty(
            ref _isProcessLockUnavailable,
            value);
    }

    public bool IsRecoveryNotScanned
    {
        get => _isRecoveryNotScanned;
        private set => SetProperty(
            ref _isRecoveryNotScanned,
            value);
    }

    public bool CanStartFileOperations
    {
        get => _canStartFileOperations;
        private set => SetProperty(
            ref _canStartFileOperations,
            value);
    }

    public bool HasRecoveryCandidates
    {
        get => _hasRecoveryCandidates;
        private set => SetProperty(
            ref _hasRecoveryCandidates,
            value);
    }

    public bool HasJournalCorruption
    {
        get => _hasJournalCorruption;
        private set => SetProperty(
            ref _hasJournalCorruption,
            value);
    }

    public bool IsRecoveryBusy
    {
        get => _isRecoveryBusy;
        private set
        {
            if (!SetProperty(
                    ref _isRecoveryBusy,
                    value))
            {
                return;
            }

            RunSafeRecoveryCommand.NotifyCanExecuteChanged();
            RefreshRecoveryCommand.NotifyCanExecuteChanged();
            RepairJournalCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsRecoveryPanelVisible
    {
        get => _isRecoveryPanelVisible;
        private set => SetProperty(
            ref _isRecoveryPanelVisible,
            value);
    }

    public bool IsRecoveryActionMessageVisible
    {
        get => _isRecoveryActionMessageVisible;
        private set => SetProperty(
            ref _isRecoveryActionMessageVisible,
            value);
    }

    public bool CanRunAutomaticRecovery
    {
        get => _canRunAutomaticRecovery;
        private set
        {
            if (SetProperty(
                    ref _canRunAutomaticRecovery,
                    value))
            {
                RunSafeRecoveryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<RecoveryCandidateViewModel> RecoveryCandidates
    {
        get => _recoveryCandidates;
        private set => SetProperty(
            ref _recoveryCandidates,
            value);
    }

    public IReadOnlyList<RecoveryResultViewModel> RecoveryResults
    {
        get => _recoveryResults;
        private set
        {
            if (SetProperty(
                    ref _recoveryResults,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasRecoveryResults));
            }
        }
    }

    public bool HasRecoveryResults =>
        RecoveryResults.Count > 0;

    public async Task RefreshDashboardAsync()
    {
        if (IsDashboardBusy)
        {
            return;
        }

        IsDashboardBusy = true;

        try
        {
            var settingsTask = _storageSettingsStore.LoadAsync();
            var analysisHistoryTask = _analysisHistoryStore.LoadLastAsync();
            var importHistoryTask = _importHistoryStore.LoadLastAsync();
            var summaryTask = _mediaIndexService.GetSummaryAsync();
            var foldersTask = _mediaIndexService.GetFolderFacetsAsync();
            var galleryTask = _mediaIndexService.SearchAsync(
                new MediaIndexSearchQuery(
                    PresentOnly: true,
                    Limit: 48,
                    MediaType: MediaFileType.Image,
                    SortOrder: MediaIndexSortOrder.FirstSeenDescending));
            var latestTask = _mediaIndexService.SearchAsync(
                new MediaIndexSearchQuery(
                    PresentOnly: true,
                    Limit: 1,
                    SortOrder: MediaIndexSortOrder.FirstSeenDescending));

            await Task.WhenAll(
                settingsTask,
                analysisHistoryTask,
                importHistoryTask,
                summaryTask,
                foldersTask,
                galleryTask,
                latestTask);

            var settings = await settingsTask;
            var lastAnalysis = await analysisHistoryTask;
            var lastImport = await importHistoryTask;
            var summary = await summaryTask;
            var folders = await foldersTask;
            var galleryCandidates = await galleryTask;
            var latestItems = await latestTask;

            GalleryMediaCountText = summary.ItemCount.ToString("N0");
            GalleryFolderCountText = folders.Count.ToString("N0");
            GalleryLastUpdatedText = FormatLastIndexed(summary.LastIndexedUtc);

            ApplyLastAnalysis(lastAnalysis);
            ApplyLastImport(lastImport);

            AnalyzeHintText = lastAnalysis is null
                ? "Wähle einen neuen Ordner für eine read-only Prüfung."
                : "Letzten Prüfpfad erneut analysieren oder eine neue Quelle wählen.";

            OrganizeHintText = folders.Count > 0
                ? $"{folders.Count:N0} Ordner bilden aktuell deine Bibliotheksstruktur."
                : "Ordner und Sammlungen erscheinen hier automatisch.";

            var endpointNames = settings.Endpoints
                .GroupBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name,
                    StringComparer.OrdinalIgnoreCase);

            var endpointRoots = BuildEndpointRoots(settings);

            await LoadRepresentativePreviewsAsync(
                SelectRepresentativeImages(galleryCandidates, 18),
                endpointRoots);

            ApplyRecentActivity(
                latestItems.FirstOrDefault(),
                endpointNames,
                summary);
        }
        catch (Exception exception)
        {
            GalleryLastUpdatedText = "Status nicht verfügbar";
            LatestMediaSubtitle = "Bibliotheksdaten konnten nicht geladen werden";
            LatestMediaAgeText = string.Empty;
            LatestFolderAgeText = string.Empty;
            LatestLibraryAgeText = string.Empty;
            Console.Error.WriteLine(
                $"Startseiten-Dashboard konnte nicht aktualisiert werden: {exception}");
        }
        finally
        {
            IsDashboardBusy = false;
        }
    }

    private Dictionary<string, string> BuildEndpointRoots(StorageSettings settings)
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in settings.Endpoints.Where(endpoint => endpoint.Enabled))
        {
            try
            {
                var resolution = _storageLocationResolver.Resolve(endpoint.Location);
                if (!resolution.IsAvailable || string.IsNullOrWhiteSpace(resolution.ResolvedPath))
                {
                    continue;
                }

                roots[endpoint.Id] = Path.GetFullPath(resolution.ResolvedPath);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or ArgumentException)
            {
                Console.Error.WriteLine(
                    $"Bibliothekspfad '{endpoint.Id}' konnte für die Startseite nicht aufgelöst werden: {exception.Message}");
            }
        }

        return roots;
    }

    private async Task LoadRepresentativePreviewsAsync(
        IReadOnlyList<MediaIndexSearchResult> items,
        IReadOnlyDictionary<string, string> endpointRoots)
    {
        var previews = new Bitmap?[5];

        var slot = 0;
        foreach (var item in items)
        {
            if (slot >= previews.Length)
            {
                break;
            }

            var file = TryResolveMediaFile(item, endpointRoots);
            if (file is null)
            {
                continue;
            }

            try
            {
                var width = slot == 0
                    ? Math.Max(_thumbnailOptions.GridWidth * 3, 480)
                    : Math.Max(_thumbnailOptions.GridWidth * 2, 320);

                var thumbnail = await _thumbnailService.GetIndexedThumbnailAsync(
                    item.Item.Id,
                    file,
                    width);

                if (!thumbnail.IsSuccessful || thumbnail.EncodedImage is not { Length: > 0 })
                {
                    continue;
                }

                using var stream = new MemoryStream(thumbnail.EncodedImage, writable: false);
                previews[slot] = new Bitmap(stream);
                slot++;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"Startseiten-Vorschau für '{item.Item.Id}' konnte nicht geladen werden: {exception.Message}");
            }
        }

        GalleryPreviewPrimary = previews[0];
        GalleryPreviewSecondary1 = previews[1];
        GalleryPreviewSecondary2 = previews[2];
        GalleryPreviewSecondary3 = previews[3];
        GalleryPreviewSecondary4 = previews[4];
    }

    private static IReadOnlyList<MediaIndexSearchResult> SelectRepresentativeImages(
        IReadOnlyList<MediaIndexSearchResult> candidates,
        int limit)
    {
        var selected = new List<MediaIndexSearchResult>(limit);
        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (!candidate.RepresentativeIsPresent
                || string.IsNullOrWhiteSpace(candidate.RepresentativeEndpointId)
                || string.IsNullOrWhiteSpace(candidate.RepresentativeRelativePath))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(candidate.RepresentativeRelativePath) ?? string.Empty;
            var folderKey = $"{candidate.RepresentativeEndpointId}|{directory}";
            if (!seenFolders.Add(folderKey))
            {
                continue;
            }

            selected.Add(candidate);
            selectedIds.Add(candidate.Item.Id);
            if (selected.Count >= limit)
            {
                return selected;
            }
        }

        foreach (var candidate in candidates)
        {
            if (!candidate.RepresentativeIsPresent
                || !selectedIds.Add(candidate.Item.Id))
            {
                continue;
            }

            selected.Add(candidate);
            if (selected.Count >= limit)
            {
                break;
            }
        }

        return selected;
    }

    private static MediaFile? TryResolveMediaFile(
        MediaIndexSearchResult result,
        IReadOnlyDictionary<string, string> endpointRoots)
    {
        if (!result.RepresentativeIsPresent
            || string.IsNullOrWhiteSpace(result.RepresentativeEndpointId)
            || string.IsNullOrWhiteSpace(result.RepresentativeRelativePath)
            || !endpointRoots.TryGetValue(result.RepresentativeEndpointId, out var root))
        {
            return null;
        }

        try
        {
            var relative = result.RepresentativeRelativePath;
            if (Path.IsPathRooted(relative))
            {
                return null;
            }

            var fullRoot = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
            var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal)
                || !File.Exists(fullPath))
            {
                return null;
            }

            var info = new FileInfo(fullPath);
            return new MediaFile(
                fullPath,
                info.Name,
                info.Extension,
                info.Length,
                info.CreationTimeUtc,
                info.LastWriteTimeUtc,
                result.Item.MediaType,
                (info.Attributes & FileAttributes.ReparsePoint) != 0);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
        {
            return null;
        }
    }

    private void ApplyLastAnalysis(MediaAnalysisHistoryEntry? entry)
    {
        if (entry is null)
        {
            LastAnalysisPathText = "Noch kein Prüfpfad gespeichert";
            LastAnalysisAgeText = "–";
            LastAnalysisSummaryText = "Noch keine Analyse gespeichert";
            return;
        }

        LastAnalysisPathText = entry.SourcePath;
        LastAnalysisAgeText = FormatAge(entry.CompletedUtc);

        var problemText = entry.ProblemCount switch
        {
            0 => "keine Probleme",
            1 => "1 Problem",
            _ => $"{entry.ProblemCount:N0} Probleme"
        };

        LastAnalysisSummaryText =
            $"{entry.MediaCount:N0} Medien · {problemText}";
    }

    private void ApplyLastImport(MediaImportHistoryEntry? entry)
    {
        if (entry is null)
        {
            LastImportDestinationText = "Noch kein Import durchgeführt";
            LastImportAgeText = "–";
            LastImportSummaryText = "Importverlauf ist noch leer";
            return;
        }

        LastImportDestinationText = string.IsNullOrWhiteSpace(entry.DestinationSubfolderName)
            ? entry.DestinationPath
            : entry.DestinationSubfolderName;
        LastImportAgeText = FormatAge(entry.CompletedUtc);

        var detailParts = new List<string>();
        if (entry.CopiedCount > 0)
        {
            detailParts.Add($"{entry.CopiedCount:N0} kopiert");
        }
        if (entry.AlreadyImportedCount > 0)
        {
            detailParts.Add($"{entry.AlreadyImportedCount:N0} bereits vorhanden");
        }
        if (entry.ConflictCount > 0)
        {
            detailParts.Add($"{entry.ConflictCount:N0} Konflikte");
        }

        LastImportSummaryText = detailParts.Count > 0
            ? string.Join(" · ", detailParts)
            : "Import abgeschlossen";
    }

    private void ApplyRecentActivity(
        MediaIndexSearchResult? latest,
        IReadOnlyDictionary<string, string> endpointNames,
        MediaIndexSummary summary)
    {
        LatestLibrarySubtitle = summary.ItemCount > 0
            ? $"{summary.ItemCount:N0} Medien im Index"
            : "Noch keine Medien im Index";
        LatestLibraryAgeText = summary.LastIndexedUtc.HasValue
            ? FormatAge(summary.LastIndexedUtc.Value)
            : string.Empty;

        if (latest is null)
        {
            LatestMediaTitle = "Noch keine Medien";
            LatestMediaSubtitle = "Importiere deine ersten Medien";
            LatestMediaAgeText = string.Empty;
            LatestFolderTitle = "Noch kein Import";
            LatestFolderSubtitle = "Der letzte Importordner erscheint hier";
            LatestFolderAgeText = string.Empty;
            LatestLibraryTitle = endpointNames.Values.FirstOrDefault() ?? "Hauptbibliothek";
            return;
        }

        var relativePath = latest.RepresentativeRelativePath ?? string.Empty;
        var fileName = string.IsNullOrWhiteSpace(relativePath)
            ? latest.Item.Id
            : Path.GetFileName(relativePath);

        LatestMediaTitle = !string.IsNullOrWhiteSpace(latest.Title)
            ? latest.Title.Trim()
            : Path.GetFileNameWithoutExtension(fileName);
        LatestMediaSubtitle = fileName;
        LatestMediaAgeText = FormatAge(latest.Item.FirstSeenUtc);

        var folder = Path.GetDirectoryName(relativePath);
        LatestFolderTitle = string.IsNullOrWhiteSpace(folder)
            ? "Bibliothekswurzel"
            : folder.Replace(Path.DirectorySeparatorChar, '/');
        LatestFolderSubtitle = "Zuletzt hinzugefügter Ordner";
        LatestFolderAgeText = FormatAge(latest.Item.FirstSeenUtc);

        LatestLibraryTitle = latest.RepresentativeEndpointId is not null
            && endpointNames.TryGetValue(latest.RepresentativeEndpointId, out var endpointName)
                ? endpointName
                : latest.RepresentativeEndpointId ?? "Hauptbibliothek";
    }

    private static string FormatLastIndexed(DateTimeOffset? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return "Noch nicht indexiert";
        }

        var local = timestamp.Value.ToLocalTime();
        var today = DateTimeOffset.Now.Date;

        if (local.Date == today)
        {
            return $"Heute, {local:HH:mm} Uhr";
        }

        if (local.Date == today.AddDays(-1))
        {
            return $"Gestern, {local:HH:mm} Uhr";
        }

        return local.ToString("dd.MM.yyyy, HH:mm");
    }

    private static string FormatAge(DateTimeOffset timestamp)
    {
        var age = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return "Gerade eben";
        }

        if (age < TimeSpan.FromHours(1))
        {
            var minutes = Math.Max(1, (int)Math.Floor(age.TotalMinutes));
            return $"Vor {minutes} Min.";
        }

        if (age < TimeSpan.FromDays(1))
        {
            var hours = Math.Max(1, (int)Math.Floor(age.TotalHours));
            return hours == 1 ? "Vor 1 Stunde" : $"Vor {hours} Stunden";
        }

        if (age < TimeSpan.FromDays(2))
        {
            return "Gestern";
        }

        var days = Math.Max(2, (int)Math.Floor(age.TotalDays));
        return days < 14
            ? $"Vor {days} Tagen"
            : timestamp.ToLocalTime().ToString("dd.MM.yyyy");
    }

    private void ReplacePreview(
        ref Bitmap? field,
        Bitmap? value,
        string propertyName)
    {
        if (ReferenceEquals(field, value))
        {
            return;
        }

        field?.Dispose();
        field = value;
        OnPropertyChanged(propertyName);
    }

    private bool CanRunSafeRecovery()
    {
        return CanRunAutomaticRecovery
            && !IsRecoveryBusy;
    }

    private bool CanRefreshRecovery()
    {
        return !IsRecoveryBusy;
    }

    private bool CanRepairJournal()
    {
        return HasJournalCorruption
            && !IsRecoveryBusy;
    }

    private async Task RepairJournalAsync()
    {
        if (!HasJournalCorruption)
        {
            return;
        }

        IsRecoveryBusy = true;
        RecoveryActionMessage =
            "Journal-Reparatur wird sicher geprüft. Nur ein eindeutig "
            + "unvollständiger letzter Datensatz darf automatisch entfernt werden.";

        try
        {
            var result =
                await _journalMaintenance.RepairTrailingRecordAsync();

            RecoveryActionMessage =
                result.Outcome switch
                {
                    OperationJournalRepairOutcome.Repaired =>
                        result.Message
                        + (string.IsNullOrWhiteSpace(result.BackupPath)
                            ? string.Empty
                            : $" Backup: {result.BackupPath}"),

                    OperationJournalRepairOutcome.NotNeeded =>
                        result.Message,

                    OperationJournalRepairOutcome.UnsafeCorruption =>
                        result.Message
                        + " Die Dateioperationen bleiben gesperrt.",

                    _ =>
                        result.Message
                        + " Die Dateioperationen bleiben gesperrt."
                };

            var refreshed =
                await _startupRecoveryService.ScanAsync();

            ApplyRecoverySnapshot(refreshed);
        }
        catch (OperationCanceledException)
        {
            RecoveryActionMessage =
                "Journal-Reparatur wurde abgebrochen.";
        }
        catch (Exception exception)
        {
            RecoveryActionMessage =
                "Journal-Reparatur konnte nicht sicher ausgeführt werden. "
                + $"Fehler: {exception.GetBaseException().Message}";

            await RefreshRecoveryAfterFailureAsync();
        }
        finally
        {
            IsRecoveryBusy = false;
        }
    }

    private async Task RunSafeRecoveryAsync()
    {
        if (!CanRunAutomaticRecovery)
        {
            return;
        }

        IsRecoveryBusy = true;
        RecoveryActionMessage =
            "Sichere Recovery wird geprüft. Es werden keine vorhandenen "
            + "Dateien überschrieben oder automatisch gelöscht.";

        try
        {
            var results =
                await _recoveryCoordinator.RecoverPendingAsync();

            RecoveryResults = results
                .Select(result =>
                    new RecoveryResultViewModel(result))
                .ToArray();

            var refreshed =
                await _startupRecoveryService.ScanAsync();

            ApplyRecoverySnapshot(refreshed);

            var successCount = results.Count(result =>
                result.Outcome is
                    FileOperationRecoveryOutcome.Recovered
                    or FileOperationRecoveryOutcome.Finalized);

            var manualCount = results.Count(result =>
                result.Outcome ==
                    FileOperationRecoveryOutcome.ManualActionRequired);

            var failedCount = results.Count(result =>
                result.Outcome ==
                    FileOperationRecoveryOutcome.Failed);

            RecoveryActionMessage =
                $"Recovery-Prüfung abgeschlossen: {successCount} sicher "
                + $"abgeschlossen, {manualCount} manuell zu prüfen, "
                + $"{failedCount} fehlgeschlagen.";
        }
        catch (OperationCanceledException)
        {
            RecoveryActionMessage =
                "Recovery-Prüfung wurde abgebrochen.";
        }
        catch (Exception exception)
        {
            RecoveryActionMessage =
                "Recovery konnte nicht sicher ausgeführt werden. "
                + $"Es wurden keine unsicheren Folgeschritte gestartet. "
                + $"Fehler: {exception.GetBaseException().Message}";

            await RefreshRecoveryAfterFailureAsync();
        }
        finally
        {
            IsRecoveryBusy = false;
        }
    }

    private async Task RefreshRecoveryAsync()
    {
        IsRecoveryBusy = true;

        try
        {
            var snapshot =
                await _startupRecoveryService.ScanAsync();

            ApplyRecoverySnapshot(snapshot);

            RecoveryActionMessage =
                snapshot.State == StartupRecoveryState.Clean
                    ? "Recovery-Status erneut geprüft: Dateisicherheit bereit."
                    : "Recovery-Status erneut geprüft. Offene oder unklare "
                      + "Zustände bleiben weiterhin gesperrt.";
        }
        catch (OperationCanceledException)
        {
            RecoveryActionMessage =
                "Recovery-Prüfung wurde abgebrochen.";
        }
        finally
        {
            IsRecoveryBusy = false;
        }
    }

    private async Task RefreshRecoveryAfterFailureAsync()
    {
        try
        {
            var snapshot =
                await _startupRecoveryService.ScanAsync();

            ApplyRecoverySnapshot(snapshot);
        }
        catch
        {
            // The original UI-level recovery error remains the useful message.
            // A failed refresh must never trigger further file operations.
        }
    }

    private void ApplyRecoverySnapshot(
        StartupRecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        RecoveryStatusTitle =
            BuildRecoveryStatusTitle(snapshot);

        RecoveryStatusMessage =
            BuildRecoveryStatusMessage(snapshot);

        IsRecoveryClean =
            snapshot.State == StartupRecoveryState.Clean;

        IsRecoveryAttentionRequired =
            snapshot.State == StartupRecoveryState.AttentionRequired;

        IsRecoveryScanFailed =
            snapshot.State == StartupRecoveryState.ScanFailed;

        IsProcessLockUnavailable =
            snapshot.State == StartupRecoveryState.ProcessLockUnavailable;

        IsRecoveryNotScanned =
            snapshot.State == StartupRecoveryState.NotScanned;

        CanStartFileOperations =
            snapshot.CanStartFileOperations;

        HasJournalCorruption =
            snapshot.HasJournalCorruption;

        RecoveryCandidates = snapshot.Candidates
            .Select(candidate =>
                new RecoveryCandidateViewModel(candidate))
            .ToArray();

        HasRecoveryCandidates =
            RecoveryCandidates.Count > 0;

        IsRecoveryPanelVisible =
            snapshot.RequiresAttention
            || snapshot.State == StartupRecoveryState.NotScanned;

        CanRunAutomaticRecovery =
            snapshot.State == StartupRecoveryState.AttentionRequired
            && HasRecoveryCandidates
            && !snapshot.HasJournalCorruption;

        RunSafeRecoveryCommand.NotifyCanExecuteChanged();
        RepairJournalCommand.NotifyCanExecuteChanged();
    }

    private static string BuildRecoveryStatusTitle(
        StartupRecoverySnapshot snapshot)
    {
        return snapshot.State switch
        {
            StartupRecoveryState.Clean =>
                "Dateisicherheit bereit",

            StartupRecoveryState.AttentionRequired =>
                "Dateioperationen gesperrt",

            StartupRecoveryState.ScanFailed =>
                "Sicherheitsprüfung fehlgeschlagen",

            StartupRecoveryState.ProcessLockUnavailable =>
                "Andere Instanz hat Dateizugriff",

            _ =>
                "Dateisicherheit wird geprüft"
        };
    }

    private static string BuildRecoveryStatusMessage(
        StartupRecoverySnapshot snapshot)
    {
        return snapshot.State switch
        {
            StartupRecoveryState.Clean =>
                "Recovery-Journal geprüft. Es sind keine offenen "
                + "Dateioperationen vorhanden.",

            StartupRecoveryState.AttentionRequired
                when snapshot.Candidates.Count > 0
                     && snapshot.CorruptJournalLineCount > 0 =>
                $"{snapshot.Candidates.Count} unvollständige Transaktion(en) "
                + $"und {snapshot.CorruptJournalLineCount} beschädigte "
                + "Journalzeile(n) müssen geprüft werden.",

            StartupRecoveryState.AttentionRequired
                when snapshot.Candidates.Count > 0 =>
                $"{snapshot.Candidates.Count} unvollständige Transaktion(en) "
                + "müssen geprüft werden, bevor Dateien verändert werden.",

            StartupRecoveryState.AttentionRequired =>
                $"{snapshot.CorruptJournalLineCount} beschädigte "
                + "Journalzeile(n) verhindern sichere Dateioperationen.",

            StartupRecoveryState.ScanFailed =>
                "Das Recovery-Journal konnte nicht zuverlässig geprüft werden. "
                + "Neue Dateioperationen bleiben aus Sicherheitsgründen gesperrt."
                + (string.IsNullOrWhiteSpace(snapshot.ErrorMessage)
                    ? string.Empty
                    : $" Fehler: {snapshot.ErrorMessage}"),

            StartupRecoveryState.ProcessLockUnavailable =>
                "Eine andere Desktop-Tools-Instanz hält den exklusiven "
                + "Dateisicherheits-Lock. Diese Instanz bleibt im Nur-Lesen-"
                + "Sicherheitszustand, bis der Lock übernommen werden kann.",

            _ =>
                "Neue Dateioperationen bleiben gesperrt, bis die "
                + "Recovery-Prüfung abgeschlossen ist."
        };
    }
}
