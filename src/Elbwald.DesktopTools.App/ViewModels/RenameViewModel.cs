using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Media.Naming;
using Elbwald.DesktopTools.Contracts.OperationHistory;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class RenameViewModel : ObservableObject
{
    private const string ReadablePresetPattern = "{date:yyyy-MM-dd}_{time:HH-mm-ss}";
    private const string ArchivePresetPattern = "{date:yyyyMMdd}_{time:HHmmss}_{seq:000}";
    private readonly IMediaSourcePicker _sourcePicker;
    private readonly IMediaScanner _mediaScanner;
    private readonly IImageMetadataReader _metadataReader;
    private readonly IMediaIndexService _mediaIndex;
    private readonly IFileOperationPlanner _fileOperationPlanner;
    private readonly IFileOperationExecutor _fileOperationExecutor;
    private readonly IMediaNamingTemplateEngine _namingTemplateEngine;
    private readonly IOperationHistoryStore _operationHistory;
    private readonly IMediaImportHistoryStore _importHistoryStore;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageLocationResolver;

    [ObservableProperty] private string sourcePath = string.Empty;
    [ObservableProperty] private bool includeSubdirectories = true;
    [ObservableProperty] private string pattern = ReadablePresetPattern;
    [ObservableProperty] private IReadOnlyList<RenamePlanItemViewModel> items = Array.Empty<RenamePlanItemViewModel>();
    [ObservableProperty] private string statusMessage = "Ordner auswählen und eine Umbenennungsregel festlegen.";
    [ObservableProperty] private string resultMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasPreview;
    [ObservableProperty] private int progressPercent;

    private string _sourceContextText = "Quelle auswählen";

    private string? _previewFingerprint;
    private MediaNamingContext? _exampleNamingContext;
    private string _exampleExtension = ".jpg";

    public RenameViewModel(
        IMediaSourcePicker sourcePicker,
        IMediaScanner mediaScanner,
        IImageMetadataReader metadataReader,
        IMediaIndexService mediaIndex,
        IFileOperationPlanner fileOperationPlanner,
        IFileOperationExecutor fileOperationExecutor,
        IMediaNamingTemplateEngine namingTemplateEngine,
        IOperationHistoryStore operationHistory,
        IMediaImportHistoryStore importHistoryStore,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver)
    {
        _sourcePicker = sourcePicker;
        _mediaScanner = mediaScanner;
        _metadataReader = metadataReader;
        _mediaIndex = mediaIndex;
        _fileOperationPlanner = fileOperationPlanner;
        _fileOperationExecutor = fileOperationExecutor;
        _namingTemplateEngine = namingTemplateEngine;
        _operationHistory = operationHistory;
        _importHistoryStore = importHistoryStore;
        _storageSettingsStore = storageSettingsStore;
        _storageLocationResolver = storageLocationResolver;

        ChooseFolderCommand = new AsyncRelayCommand(ChooseFolderAsync, () => !IsBusy);
        CreatePreviewCommand = new AsyncRelayCommand(CreatePreviewAsync, CanCreatePreview);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, CanExecute);
        UseArchivePresetCommand = new RelayCommand(() => Pattern = ArchivePresetPattern);
        UseReadablePresetCommand = new RelayCommand(() => Pattern = ReadablePresetPattern);
        UseOriginalPresetCommand = new RelayCommand(() => Pattern = "{date:yyyy-MM-dd}_{original}");
        UseCameraPresetCommand = new RelayCommand(() => Pattern = "{date:yyyy-MM-dd}_{camera}_{seq:000}");
        UseTitlePresetCommand = new RelayCommand(() => Pattern = "{date:yyyy-MM-dd}_{title}_{seq:000}");
        InsertTokenCommand = new RelayCommand<string>(
            token =>
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    Pattern += token;
                }
            });

        _ = LoadSuggestedSourceAsync();
    }

    public IAsyncRelayCommand ChooseFolderCommand { get; }
    public IAsyncRelayCommand CreatePreviewCommand { get; }
    public IAsyncRelayCommand ExecuteCommand { get; }
    public IRelayCommand UseArchivePresetCommand { get; }
    public IRelayCommand UseReadablePresetCommand { get; }
    public IRelayCommand UseOriginalPresetCommand { get; }
    public IRelayCommand UseCameraPresetCommand { get; }
    public IRelayCommand UseTitlePresetCommand { get; }
    public IRelayCommand<string> InsertTokenCommand { get; }

    public int ReadyCount => Items.Count(x => x.IsReady);
    public int ConflictCount => Items.Count(x => string.Equals(x.Status, "Konflikt", StringComparison.Ordinal));
    public int UnchangedCount => Items.Count(x => string.Equals(x.Status, "Unverändert", StringComparison.Ordinal));
    public string ReadyText => $"Wird umbenannt: {ReadyCount:N0}";
    public string ConflictText => $"Konflikte: {ConflictCount:N0}";
    public string UnchangedText => $"Bleibt unverändert: {UnchangedCount:N0}";
    public bool HasResult => !string.IsNullOrWhiteSpace(ResultMessage);
    public bool CanRun => HasPreview && ReadyCount > 0 && ConflictCount == 0 && !IsBusy;

    public string SourceContextText
    {
        get => _sourceContextText;
        private set => SetProperty(ref _sourceContextText, value);
    }

    public string RuleName => Pattern switch
    {
        ArchivePresetPattern => "Archiv · Datum + Nummer",
        ReadablePresetPattern => "Datum + Uhrzeit",
        "{date:yyyy-MM-dd}_{original}" => "Datum + Originalname",
        "{date:yyyy-MM-dd}_{camera}_{seq:000}" => "Datum + Kamera",
        "{date:yyyy-MM-dd}_{title}_{seq:000}" => "Datum + Titel",
        _ => "Eigene Regel"
    };

    public string ExampleFileName
    {
        get
        {
            if (HasPreview)
            {
                var previewItem = Items.FirstOrDefault(item => !item.IsCompanion);
                if (previewItem is not null)
                {
                    return previewItem.TargetName;
                }
            }

            if (_exampleNamingContext is null)
            {
                return "Vorschau erstellen – dann siehst du ein echtes Beispiel aus deiner Quelle";
            }

            try
            {
                var stem = _namingTemplateEngine.RenderFileStem(
                    Pattern,
                    _exampleNamingContext);

                return stem + _exampleExtension;
            }
            catch
            {
                return "Regel bitte prüfen";
            }
        }
    }

    public bool NeedsPreview => !HasPreview;

    public string PreviewActionText => HasPreview ? "Vorschau aktualisieren" : "Vorschau erstellen";

    public string ExecuteActionText => ReadyCount switch
    {
        0 => "Dateien umbenennen",
        1 => "1 Datei umbenennen",
        _ => $"{ReadyCount:N0} Dateien umbenennen"
    };

    partial void OnSourcePathChanged(string value)
    {
        _exampleNamingContext = null;
        _exampleExtension = ".jpg";
        OnPropertyChanged(nameof(ExampleFileName));
        InvalidatePreview();
    }
    partial void OnIncludeSubdirectoriesChanged(bool value) => InvalidatePreview();
    partial void OnPatternChanged(string value)
    {
        InvalidatePreview();
        OnPropertyChanged(nameof(RuleName));
        OnPropertyChanged(nameof(ExampleFileName));
    }
    partial void OnResultMessageChanged(string value) => OnPropertyChanged(nameof(HasResult));

    partial void OnItemsChanged(IReadOnlyList<RenamePlanItemViewModel> value)
    {
        NotifySummaryChanged();
    }

    partial void OnHasPreviewChanged(bool value)
    {
        NotifySummaryChanged();
        OnPropertyChanged(nameof(PreviewActionText));
        OnPropertyChanged(nameof(NeedsPreview));
        OnPropertyChanged(nameof(ExampleFileName));
        ExecuteCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        ChooseFolderCommand.NotifyCanExecuteChanged();
        CreatePreviewCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(ExecuteActionText));
    }

    private async Task LoadSuggestedSourceAsync()
    {
        try
        {
            var lastImport = await _importHistoryStore.LoadLastAsync();
            var settings = await _storageSettingsStore.LoadAsync();
            var importEndpoint = settings.Find(StorageEndpoint.DefaultImportId);

            string? importRoot = null;
            if (importEndpoint is not null)
            {
                var resolution = _storageLocationResolver.Resolve(importEndpoint.Location);
                if (resolution.IsAvailable)
                {
                    importRoot = resolution.ResolvedPath;
                }
            }

            if (lastImport is not null
                && !string.IsNullOrWhiteSpace(lastImport.DestinationPath)
                && Directory.Exists(lastImport.DestinationPath))
            {
                SourcePath = lastImport.DestinationPath;
                SourceContextText = "Zuletzt importierter Ordner";
                StatusMessage = "Der zuletzt importierte Ordner ist bereits ausgewählt. Wähle einen Namensstil und erstelle eine Vorschau.";
            }
            else if (!string.IsNullOrWhiteSpace(importRoot)
                     && Directory.Exists(importRoot))
            {
                SourcePath = importRoot;
                SourceContextText = "Import-Wartehalle";
                StatusMessage = "Die Import-Wartehalle ist ausgewählt. Du kannst jederzeit auch einen anderen Ordner verwenden.";
            }
            else
            {
                SourceContextText = "Quelle auswählen";
            }
        }
        catch
        {
            SourceContextText = "Quelle auswählen";
            StatusMessage = "Der vorgeschlagene Ordner konnte nicht geladen werden. Du kannst die Quelle manuell auswählen.";
        }
    }

    private async Task ChooseFolderAsync()
    {
        var folder = await _sourcePicker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SourcePath = Path.GetFullPath(folder);
            SourceContextText = "Eigener Ordner";
            StatusMessage = "Quelle gewählt. Die Vorschau zeigt dir vorab jeden neuen Dateinamen.";
        }
    }

    private bool CanCreatePreview() =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(SourcePath)
        && !string.IsNullOrWhiteSpace(Pattern);

    private bool CanExecute() => CanRun;

    private async Task CreatePreviewAsync()
    {
        IsBusy = true;
        HasPreview = false;
        ResultMessage = string.Empty;
        Items = Array.Empty<RenamePlanItemViewModel>();
        ProgressPercent = 0;
        _previewFingerprint = null;
        StatusMessage = "Quelle wird vollständig geprüft …";

        try
        {
            if (!Directory.Exists(SourcePath))
            {
                StatusMessage = "Der ausgewählte Quellordner ist nicht verfügbar.";
                return;
            }

            _namingTemplateEngine.Validate(Pattern);

            var scan = await _mediaScanner.ScanAsync(
                SourcePath,
                new MediaScanOptions
                {
                    Recursive = IncludeSubdirectories,
                    IncludeUnknownFiles = false
                });

            if (scan.HasErrors || scan.SkippedSymbolicLinkDirectories > 0)
            {
                StatusMessage = "Vorschau blockiert: Die Quelle konnte nicht vollständig und eindeutig gelesen werden.";
                return;
            }

            var mediaFiles = scan.Files
                .Where(x => x.MediaType != MediaFileType.Unknown)
                .OrderBy(x => x.FullPath, GetPathComparer())
                .ToArray();

            if (mediaFiles.Length == 0)
            {
                StatusMessage = "Keine unterstützten Mediendateien gefunden.";
                return;
            }

            if (mediaFiles.Any(x => x.IsSymbolicLink))
            {
                StatusMessage = "Vorschau blockiert: Symbolische Links werden beim Umbenennen nicht verarbeitet.";
                return;
            }

            var groups = mediaFiles
                .GroupBy(
                    x => BuildGroupKey(x.DirectoryPath, Path.GetFileNameWithoutExtension(x.FileName)),
                    GetPathComparer())
                .OrderBy(x => x.Key, GetPathComparer())
                .ToArray();

            var previewItems = new List<RenamePlanItemViewModel>();
            var requests = new List<FileOperationRequest>();
            var requestItems = new List<RenamePlanItemViewModel>();
            var sequence = 1;
            var reservedTargetPaths = new HashSet<string>(GetPathComparer());

            foreach (var group in groups)
            {
                var members = group.OrderBy(x => x.FileName, GetPathComparer()).ToArray();
                var metadataContext = await LoadMetadataContextAsync(members[0]);
                var originalStem = Path.GetFileNameWithoutExtension(members[0].FileName);
                var namingContext = new MediaNamingContext(
                    metadataContext.CaptureDate,
                    originalStem,
                    members[0].Extension,
                    members[0].MediaType.ToString(),
                    Path.GetFileName(members[0].DirectoryPath),
                    metadataContext.CameraMake,
                    metadataContext.CameraModel,
                    metadataContext.Title,
                    metadataContext.Description,
                    metadataContext.Width,
                    metadataContext.Height,
                    sequence);

                if (_exampleNamingContext is null)
                {
                    _exampleNamingContext = namingContext;
                    _exampleExtension = members[0].Extension;
                    OnPropertyChanged(nameof(ExampleFileName));
                }

                var targetStem = _namingTemplateEngine.RenderFileStem(
                    Pattern,
                    namingContext);

                if (string.Equals(Pattern, ReadablePresetPattern, StringComparison.Ordinal))
                {
                    targetStem = ResolveReadablePresetCollision(
                        targetStem,
                        members,
                        reservedTargetPaths);
                }

                foreach (var member in members)
                {
                    reservedTargetPaths.Add(Path.GetFullPath(
                        Path.Combine(member.DirectoryPath, targetStem + member.Extension)));
                }

                foreach (var member in members)
                {
                    var targetName = targetStem + member.Extension;
                    var targetPath = Path.Combine(member.DirectoryPath, targetName);
                    var item = await CreateItemAsync(
                        member.FullPath,
                        targetPath,
                        metadataContext.DateSource,
                        isCompanion: false);

                    previewItems.Add(item);
                    AddRequestIfNeeded(item, requests, requestItems);
                }

                foreach (var sidecar in FindXmpCompanions(members, originalStem))
                {
                    var sidecarTargetName = sidecar.Style switch
                    {
                        XmpStyle.Stem => targetStem + ".xmp",
                        XmpStyle.FullFileName => targetStem + sidecar.MediaExtension + ".xmp",
                        _ => throw new InvalidOperationException("Unbekannte XMP-Zuordnung.")
                    };

                    var sidecarTargetPath = Path.Combine(sidecar.DirectoryPath, sidecarTargetName);
                    var item = await CreateItemAsync(
                        sidecar.SourcePath,
                        sidecarTargetPath,
                        "XMP-Begleitdatei",
                        isCompanion: true);

                    previewItems.Add(item);
                    AddRequestIfNeeded(item, requests, requestItems);
                }

                sequence++;
            }

            var plan = _fileOperationPlanner.CreatePlan(requests);
            ApplyPlanConflicts(plan, requestItems);

            Items = previewItems;
            HasPreview = true;
            _previewFingerprint = CreateFingerprint(previewItems);
            NotifySummaryChanged();

            StatusMessage = ConflictCount > 0
                ? $"Vorschau erstellt: {ReadyCount:N0} Dateien würden umbenannt, {ConflictCount:N0} Konflikte müssen vorher geklärt werden."
                : $"Vorschau erstellt: {ReadyCount:N0} Dateien werden umbenannt, {UnchangedCount:N0} bleiben unverändert.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException
                or CryptographicException
                or FormatException)
        {
            Items = Array.Empty<RenamePlanItemViewModel>();
            HasPreview = false;
            _previewFingerprint = null;
            StatusMessage = "Vorschau konnte nicht erstellt werden: " + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
            CreatePreviewCommand.NotifyCanExecuteChanged();
            ExecuteCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ExecuteAsync()
    {
        if (!CanRun || _previewFingerprint is null)
        {
            return;
        }

        IsBusy = true;
        ResultMessage = string.Empty;
        ProgressPercent = 0;
        StatusMessage = "Sicherheitsprüfung vor der Umbenennung …";

        try
        {
            var readyItems = Items.Where(x => x.IsReady).ToArray();
            if (readyItems.Length == 0)
            {
                StatusMessage = "Keine Änderungen auszuführen.";
                return;
            }

            if (!string.Equals(_previewFingerprint, CreateFingerprint(Items), StringComparison.Ordinal))
            {
                StatusMessage = "Ausführung blockiert: Die Vorschau hat sich verändert. Bitte neu erstellen.";
                return;
            }

            foreach (var item in readyItems)
            {
                await RevalidateSourceAsync(item);
                if (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
                {
                    throw new InvalidOperationException(
                        $"Ziel wurde seit der Vorschau angelegt: {item.TargetPath}");
                }
            }

            var requests = readyItems
                .Select(x => FileOperationRequest.Move(x.SourcePath, x.TargetPath))
                .ToArray();

            var plan = _fileOperationPlanner.CreatePlan(requests);
            if (!plan.CanExecute)
            {
                StatusMessage = "Ausführung blockiert: Der Dateioperationsplan ist nicht mehr konfliktfrei. Bitte Vorschau neu erstellen.";
                return;
            }

            var progress = new Progress<FileOperationProgress>(p =>
            {
                ProgressPercent = p.Percentage;
                StatusMessage = $"Umbenennen: {p.CompletedCount:N0} von {p.TotalCount:N0} …";
            });

            // Nach Abschluss der vollständigen Vorprüfung gibt es bewusst keinen UI-Abbruch mehr.
            var result = await _fileOperationExecutor.ExecuteAsync(
                plan,
                progress,
                CancellationToken.None);

            if (result.IsSuccessful)
            {
                var completedUtc = DateTimeOffset.UtcNow;
                var historyBatchId = result.TransactionId?.ToString("D") ?? Guid.NewGuid().ToString("D");
                var historyEntries = new List<OperationHistoryEntry>(readyItems.Length);
                foreach (var item in readyItems)
                {
                    string? mediaItemId = null;
                    var indexed = await _mediaIndex.FindByHashAsync(item.Sha256, item.Length);
                    if (indexed is not null)
                    {
                        mediaItemId = indexed.Id;
                    }

                    historyEntries.Add(new OperationHistoryEntry(
                        historyBatchId,
                        "Rename",
                        item.SourcePath,
                        item.TargetPath,
                        item.Sha256,
                        item.Length,
                        mediaItemId,
                        completedUtc));
                }

                await _operationHistory.AppendBatchAsync(historyEntries);

                ProgressPercent = 100;
                ResultMessage = $"Umbenennung abgeschlossen: {result.CompletedCount:N0} Dateien wurden sicher umbenannt.";
                StatusMessage = "Fertig. Die Bibliothek erkennt die Dateien beim nächsten Aktualisieren automatisch wieder.";
                HasPreview = false;
                _previewFingerprint = null;
                return;
            }

            ResultMessage = result.RequiresRecovery
                ? "Umbenennung nicht vollständig abgeschlossen. Recovery ist erforderlich; weitere Dateioperationen bleiben geschützt."
                : result.WasBlockedByRecoveryState
                    ? "Umbenennung blockiert: Zuerst offenen Recovery-Zustand klären."
                    : result.WasBlockedByProcessLock
                        ? "Umbenennung blockiert: Eine andere Dateioperation ist aktiv."
                        : "Umbenennung wurde nicht vollständig ausgeführt: " + (result.ErrorMessage ?? result.State.ToString());

            StatusMessage = ResultMessage;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or CryptographicException
                or ArgumentException)
        {
            ResultMessage = "Ausführung blockiert: " + exception.GetBaseException().Message;
            StatusMessage = ResultMessage;
        }
        finally
        {
            IsBusy = false;
            NotifySummaryChanged();
        }
    }

    private async Task<MetadataContext> LoadMetadataContextAsync(MediaFile file)
    {
        DateTime? capturedAt = null;
        string? cameraMake = null;
        string? cameraModel = null;
        string? title = null;
        string? description = null;
        int? width = null;
        int? height = null;
        var dateSource = "Dateisystem";

        if (file.IsImage)
        {
            var metadataResult = await _metadataReader.ReadAsync(file);
            if (metadataResult.Metadata is { } metadata)
            {
                capturedAt = metadata.CapturedAt;
                cameraMake = metadata.CameraMake;
                cameraModel = metadata.CameraModel;
                width = metadata.PixelWidth;
                height = metadata.PixelHeight;
                if (capturedAt.HasValue) dateSource = "EXIF";
            }
        }

        var sha256 = await ComputeSha256Async(file.FullPath);
        var indexedItem = await _mediaIndex.FindByHashAsync(sha256, file.Length);
        if (indexedItem is not null)
        {
            var dbMetadata = await _mediaIndex.GetMetadataAsync(indexedItem.Id);
            if (dbMetadata.CaptureDateOverridden && dbMetadata.CaptureDate.HasValue)
            {
                capturedAt = dbMetadata.CaptureDate;
                dateSource = "Elbwald-Metadaten";
            }
            if (dbMetadata.TitleOverridden) title = dbMetadata.Title;
            if (dbMetadata.DescriptionOverridden) description = dbMetadata.Description;
        }

        capturedAt ??= file.LastWriteTimeUtc.LocalDateTime;
        return new MetadataContext(capturedAt.Value, cameraMake, cameraModel, title, description, width, height, dateSource);
    }

    private async Task<RenamePlanItemViewModel> CreateItemAsync(
        string sourcePath,
        string targetPath,
        string dateSource,
        bool isCompanion)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Quelldatei wurde während der Vorschau entfernt.", sourcePath);
        }

        var hash = await ComputeSha256Async(sourcePath);
        var same = GetPathComparer().Equals(
            Path.GetFullPath(sourcePath),
            Path.GetFullPath(targetPath));

        return new RenamePlanItemViewModel
        {
            SourcePath = Path.GetFullPath(sourcePath),
            SourceName = Path.GetFileName(sourcePath),
            TargetName = Path.GetFileName(targetPath),
            TargetPath = Path.GetFullPath(targetPath),
            Status = same ? "Unverändert" : "Bereit",
            Message = same ? "Regel erzeugt denselben Namen." : "Geprüft und für den Rename-Plan vorgesehen.",
            DateSource = dateSource,
            Length = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc,
            Sha256 = hash,
            IsCompanion = isCompanion,
            IsReady = !same
        };
    }

    private static void AddRequestIfNeeded(
        RenamePlanItemViewModel item,
        ICollection<FileOperationRequest> requests,
        ICollection<RenamePlanItemViewModel> requestItems)
    {
        if (!item.IsReady)
        {
            return;
        }

        requests.Add(FileOperationRequest.Move(item.SourcePath, item.TargetPath));
        requestItems.Add(item);
    }

    private static void ApplyPlanConflicts(
        FileOperationPlan plan,
        IReadOnlyList<RenamePlanItemViewModel> requestItems)
    {
        foreach (var conflict in plan.Conflicts)
        {
            if (conflict.OperationIndex < 0 || conflict.OperationIndex >= requestItems.Count)
            {
                continue;
            }

            var item = requestItems[conflict.OperationIndex];
            item.Status = "Konflikt";
            item.IsReady = false;
            item.Message = conflict.Kind switch
            {
                FileOperationConflictKind.DestinationAlreadyExists => "Zieldatei existiert bereits.",
                FileOperationConflictKind.DuplicateDestination => "Mehrere Dateien würden denselben Zielnamen erhalten.",
                FileOperationConflictKind.DestinationIsAnotherSource => "Das Ziel ist zugleich eine andere Quelldatei. Ketten-/Tausch-Renames werden fail-closed blockiert.",
                FileOperationConflictKind.SameSourceAndDestination => "Quelle und Ziel sind identisch.",
                FileOperationConflictKind.SourceFileMissing => "Quelldatei fehlt.",
                _ => "Dateioperationskonflikt: " + conflict.Kind
            };
        }
    }

    private async Task RevalidateSourceAsync(RenamePlanItemViewModel item)
    {
        var info = new FileInfo(item.SourcePath);
        if (!info.Exists)
        {
            throw new InvalidOperationException($"Quelle fehlt seit der Vorschau: {item.SourcePath}");
        }

        if (info.Length != item.Length || info.LastWriteTimeUtc != item.LastWriteTimeUtc)
        {
            throw new InvalidOperationException($"Quelle wurde seit der Vorschau verändert: {item.SourcePath}");
        }

        var hash = await ComputeSha256Async(item.SourcePath);
        if (!string.Equals(hash, item.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SHA-256 hat sich seit der Vorschau verändert: {item.SourcePath}");
        }
    }

    private static string ResolveReadablePresetCollision(
        string baseStem,
        IReadOnlyList<MediaFile> members,
        ISet<string> reservedTargetPaths)
    {
        var sourcePaths = new HashSet<string>(
            members.Select(member => Path.GetFullPath(member.FullPath)),
            GetPathComparer());

        for (var candidateNumber = 1; candidateNumber < 10000; candidateNumber++)
        {
            var candidateStem = candidateNumber == 1
                ? baseStem
                : $"{baseStem}_{candidateNumber:000}";

            var hasCollision = members.Any(member =>
            {
                var targetPath = Path.GetFullPath(
                    Path.Combine(member.DirectoryPath, candidateStem + member.Extension));

                if (reservedTargetPaths.Contains(targetPath))
                {
                    return true;
                }

                return File.Exists(targetPath)
                    && !sourcePaths.Contains(targetPath);
            });

            if (!hasCollision)
            {
                return candidateStem;
            }
        }

        throw new InvalidOperationException(
            "Für den vorgeschlagenen Dateinamen konnte kein freier Name gefunden werden.");
    }

    private static IReadOnlyList<XmpCompanion> FindXmpCompanions(
        IReadOnlyList<MediaFile> members,
        string originalStem)
    {
        var result = new Dictionary<string, XmpCompanion>(GetPathComparer());
        var directory = members[0].DirectoryPath;

        var stemPath = FindExistingFileIgnoreCase(directory, originalStem + ".xmp");
        if (stemPath is not null)
        {
            result[Path.GetFullPath(stemPath)] = new XmpCompanion(
                Path.GetFullPath(stemPath),
                directory,
                XmpStyle.Stem,
                string.Empty);
        }

        foreach (var member in members)
        {
            var fullNamePath = FindExistingFileIgnoreCase(
                directory,
                member.FileName + ".xmp");
            if (fullNamePath is null)
            {
                continue;
            }

            result[Path.GetFullPath(fullNamePath)] = new XmpCompanion(
                Path.GetFullPath(fullNamePath),
                directory,
                XmpStyle.FullFileName,
                member.Extension);
        }

        return result.Values.OrderBy(x => x.SourcePath, GetPathComparer()).ToArray();
    }

    private static string? FindExistingFileIgnoreCase(string directory, string fileName)
    {
        var exact = Path.Combine(directory, fileName);
        if (File.Exists(exact))
        {
            return exact;
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(
                Path.GetFileName(path),
                fileName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildGroupKey(string directory, string stem) =>
        Path.GetFullPath(directory) + Path.DirectorySeparatorChar + stem;

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static string CreateFingerprint(IEnumerable<RenamePlanItemViewModel> items)
    {
        var text = string.Join(
            "\n",
            items.OrderBy(x => x.SourcePath, GetPathComparer())
                .Select(x => string.Join("|", x.SourcePath, x.TargetPath, x.Length, x.LastWriteTimeUtc.Ticks, x.Sha256, x.IsReady)));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private void InvalidatePreview()
    {
        HasPreview = false;
        _previewFingerprint = null;
        ResultMessage = string.Empty;
        ProgressPercent = 0;
        CreatePreviewCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(ReadyCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(UnchangedCount));
        OnPropertyChanged(nameof(ReadyText));
        OnPropertyChanged(nameof(ConflictText));
        OnPropertyChanged(nameof(UnchangedText));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(ExecuteActionText));
        ExecuteCommand.NotifyCanExecuteChanged();
    }

    private sealed record MetadataContext(
        DateTime CaptureDate,
        string? CameraMake,
        string? CameraModel,
        string? Title,
        string? Description,
        int? Width,
        int? Height,
        string DateSource);

    private sealed record XmpCompanion(
        string SourcePath,
        string DirectoryPath,
        XmpStyle Style,
        string MediaExtension);

    private enum XmpStyle
    {
        Stem,
        FullFileName
    }
}
