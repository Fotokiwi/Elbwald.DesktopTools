using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Exporting;
using Elbwald.DesktopTools.Contracts.Media.Knowledge;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;
using Elbwald.DesktopTools.Contracts.Media.MetadataExporting;
using Elbwald.DesktopTools.Contracts.Media.Naming;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.OperationHistory;
using Elbwald.DesktopTools.Contracts.Projects;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MediaExportViewModel : ObservableObject
{
    private const string SelectionSource = "Aktuelle Bibliotheksauswahl";
    private const string ProjectSource = "Projekt";
    private const string OriginalFormat = "Originalformat behalten";

    private readonly IMediaIndexService _mediaIndex;
    private readonly IStorageSettingsStore _storageSettings;
    private readonly IStorageLocationResolver _storageResolver;
    private readonly IMediaSourcePicker _picker;
    private readonly IProjectService _projects;
    private readonly IMediaNamingTemplateEngine _naming;
    private readonly IMetadataExportService _metadataExport;
    private readonly IImageExportRegistry _imageExportRegistry;
    private readonly IOperationHistoryStore _history;
    private readonly MediaExportSelectionContext _selectionContext;

    private CancellationTokenSource? _executionCts;
    private StorageLocation? _plannedTargetLocation;
    private bool _suppressInvalidate;

    [ObservableProperty] private string selectedSourceMode = SelectionSource;
    [ObservableProperty] private IReadOnlyList<MediaExportProjectOptionViewModel> projectOptions = Array.Empty<MediaExportProjectOptionViewModel>();
    [ObservableProperty] private MediaExportProjectOptionViewModel? selectedProject;
    [ObservableProperty] private int librarySelectionCount;
    [ObservableProperty] private string targetDirectory = string.Empty;
    [ObservableProperty] private string directoryPattern = "{year}/{month}";
    [ObservableProperty] private string fileNamePattern = "{date:yyyy-MM-dd}_{original}";
    [ObservableProperty] private IReadOnlyList<string> formatOptions = [OriginalFormat];
    [ObservableProperty] private string selectedFormat = OriginalFormat;
    [ObservableProperty] private string selectedMaxEdge = "Originalgröße";
    [ObservableProperty] private int exportQuality = 92;
    [ObservableProperty] private bool embedXmp;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Quelle und Ziel wählen, dann eine Exportvorschau erzeugen.";
    [ObservableProperty] private IReadOnlyList<MediaExportPlanItemViewModel> planItems = Array.Empty<MediaExportPlanItemViewModel>();
    [ObservableProperty] private bool hasPlan;
    [ObservableProperty] private int readyCount;
    [ObservableProperty] private int blockedCount;
    [ObservableProperty] private long estimatedBytes;
    [ObservableProperty] private double progressPercent;
    [ObservableProperty] private string progressText = string.Empty;
    [ObservableProperty] private string resultMessage = string.Empty;

    public MediaExportViewModel(
        IMediaIndexService mediaIndex,
        IStorageSettingsStore storageSettings,
        IStorageLocationResolver storageResolver,
        IMediaSourcePicker picker,
        IProjectService projects,
        IMediaNamingTemplateEngine naming,
        IMetadataExportService metadataExport,
        IImageExportRegistry imageExportRegistry,
        IOperationHistoryStore history,
        MediaExportSelectionContext selectionContext)
    {
        _mediaIndex = mediaIndex ?? throw new ArgumentNullException(nameof(mediaIndex));
        _storageSettings = storageSettings ?? throw new ArgumentNullException(nameof(storageSettings));
        _storageResolver = storageResolver ?? throw new ArgumentNullException(nameof(storageResolver));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        _metadataExport = metadataExport ?? throw new ArgumentNullException(nameof(metadataExport));
        _imageExportRegistry = imageExportRegistry ?? throw new ArgumentNullException(nameof(imageExportRegistry));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _selectionContext = selectionContext ?? throw new ArgumentNullException(nameof(selectionContext));

        ChooseTargetCommand = new AsyncRelayCommand(ChooseTargetAsync, () => !IsBusy);
        RefreshSourcesCommand = new AsyncRelayCommand(LoadSourceOptionsAsync, () => !IsBusy);
        BuildPreviewCommand = new AsyncRelayCommand(BuildPreviewAsync, () => !IsBusy);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, CanExecute);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy && _executionCts is not null);
        InsertDirectoryTokenCommand = new RelayCommand<string>(token => InsertToken(token, isDirectory: true));
        InsertFileTokenCommand = new RelayCommand<string>(token => InsertToken(token, isDirectory: false));
        ApplyPresetCommand = new RelayCommand<string>(ApplyPreset);

        RefreshFormatOptions();
        _ = LoadSourceOptionsAsync();
    }

    public IReadOnlyList<string> SourceModes { get; } = [SelectionSource, ProjectSource];
    public IReadOnlyList<string> MaxEdgeOptions { get; } = ["Originalgröße", "1920 px", "2560 px", "3000 px", "3840 px"];
    public IReadOnlyList<int> QualityOptions { get; } = [80, 85, 90, 92, 95, 100];

    public IAsyncRelayCommand ChooseTargetCommand { get; }
    public IAsyncRelayCommand RefreshSourcesCommand { get; }
    public IAsyncRelayCommand BuildPreviewCommand { get; }
    public IAsyncRelayCommand ExecuteCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand<string> InsertDirectoryTokenCommand { get; }
    public IRelayCommand<string> InsertFileTokenCommand { get; }
    public IRelayCommand<string> ApplyPresetCommand { get; }

    public bool IsInputEnabled => !IsBusy;
    public bool IsProjectSource => string.Equals(SelectedSourceMode, ProjectSource, StringComparison.Ordinal);
    public bool IsSelectionSource => !IsProjectSource;
    public bool IsOriginalExport => string.Equals(SelectedFormat, OriginalFormat, StringComparison.Ordinal);
    public bool CanResize => ResolveSelectedExporter()?.SupportsResize == true;
    public bool CanSelectQuality => ResolveSelectedExporter()?.SupportsQuality == true;
    public bool IsJpegExport => string.Equals(
        ResolveSelectedExporter()?.FormatId,
        AvaloniaJpegImageExportProvider.JpegFormatId,
        StringComparison.OrdinalIgnoreCase);
    public string SelectionSummary => LibrarySelectionCount == 0
        ? "Keine Bibliotheksauswahl vorgemerkt"
        : $"{LibrarySelectionCount:N0} Medium/Medien aus der Bibliothek";
    public string EstimatedSizeText => FormatBytes(EstimatedBytes);
    public bool HasBlockedItems => BlockedCount > 0;
    public bool HasResult => !string.IsNullOrWhiteSpace(ResultMessage);

    partial void OnSelectedSourceModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsProjectSource));
        OnPropertyChanged(nameof(IsSelectionSource));
        InvalidatePlan();
    }

    partial void OnSelectedProjectChanged(MediaExportProjectOptionViewModel? value) => InvalidatePlan();
    partial void OnTargetDirectoryChanged(string value) => InvalidatePlan();
    partial void OnDirectoryPatternChanged(string value) => InvalidatePlan();
    partial void OnFileNamePatternChanged(string value) => InvalidatePlan();
    partial void OnSelectedMaxEdgeChanged(string value) => InvalidatePlan();
    partial void OnEmbedXmpChanged(bool value) => InvalidatePlan();
    partial void OnExportQualityChanged(int value) => InvalidatePlan();

    partial void OnSelectedFormatChanged(string value)
    {
        OnPropertyChanged(nameof(IsJpegExport));
        OnPropertyChanged(nameof(IsOriginalExport));
        OnPropertyChanged(nameof(CanResize));
        OnPropertyChanged(nameof(CanSelectQuality));
        if (!CanResize)
        {
            _suppressInvalidate = true;
            SelectedMaxEdge = "Originalgröße";
            _suppressInvalidate = false;
        }
        InvalidatePlan();
    }

    partial void OnIsBusyChanged(bool value)
    {
        ChooseTargetCommand.NotifyCanExecuteChanged();
        RefreshSourcesCommand.NotifyCanExecuteChanged();
        BuildPreviewCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsInputEnabled));
    }

    partial void OnHasPlanChanged(bool value) => ExecuteCommand.NotifyCanExecuteChanged();
    partial void OnBlockedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasBlockedItems));
        ExecuteCommand.NotifyCanExecuteChanged();
    }
    partial void OnEstimatedBytesChanged(long value) => OnPropertyChanged(nameof(EstimatedSizeText));
    partial void OnLibrarySelectionCountChanged(int value) => OnPropertyChanged(nameof(SelectionSummary));
    partial void OnResultMessageChanged(string value) => OnPropertyChanged(nameof(HasResult));

    private async Task LoadSourceOptionsAsync()
    {
        if (IsBusy) return;

        try
        {
            _suppressInvalidate = true;
            LibrarySelectionCount = _selectionContext.Count;
            var projectRecords = await _projects.GetProjectsAsync();
            var options = new List<MediaExportProjectOptionViewModel>();
            foreach (var project in projectRecords.OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var refs = await _projects.GetMediaReferencesAsync(project.Id);
                options.Add(new MediaExportProjectOptionViewModel(project.Id, project.Name, refs.Count));
            }
            ProjectOptions = options;
            if (SelectedProject is null || !options.Any(option => option.Id == SelectedProject.Id))
            {
                SelectedProject = options.FirstOrDefault();
            }
            if (LibrarySelectionCount == 0 && options.Count > 0)
            {
                SelectedSourceMode = ProjectSource;
            }
            _suppressInvalidate = false;
            InvalidatePlan();
        }
        catch (Exception exception)
        {
            _suppressInvalidate = false;
            StatusMessage = "Quellen konnten nicht geladen werden: " + exception.GetBaseException().Message;
        }
    }

    private async Task ChooseTargetAsync()
    {
        var folder = await _picker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            TargetDirectory = Path.GetFullPath(folder);
        }
    }

    private async Task BuildPreviewAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ResultMessage = string.Empty;
        try
        {
            InvalidatePlan();
            var ids = await GetSourceIdsAsync();
            if (ids.Count == 0)
            {
                StatusMessage = IsProjectSource
                    ? "Das ausgewählte Projekt enthält keine Medien."
                    : "In der Bibliothek ist aktuell nichts für den Export ausgewählt.";
                return;
            }

            if (string.IsNullOrWhiteSpace(TargetDirectory) || !Directory.Exists(TargetDirectory))
            {
                StatusMessage = "Bitte zuerst einen vorhandenen Export-Zielordner auswählen.";
                return;
            }

            var targetInfo = new DirectoryInfo(TargetDirectory);
            if ((targetInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                StatusMessage = "Der Export-Zielordner ist ein Symlink/Reparse-Point und wird aus Sicherheitsgründen nicht verwendet.";
                return;
            }

            try
            {
                _plannedTargetLocation = _storageResolver.Capture(TargetDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                StatusMessage = "Exportziel kann nicht über eine stabile Datenträger-ID abgesichert werden: " + exception.GetBaseException().Message;
                return;
            }

            _naming.Validate(FileNamePattern);
            ValidateDirectoryPattern(DirectoryPattern);

            var selectedExporter = ResolveSelectedExporter();
            if (!IsOriginalExport && selectedExporter is null)
            {
                StatusMessage = "Das gewählte Ausgabeformat ist nicht mehr aktiv oder verfügbar.";
                return;
            }

            var roots = await ResolveEndpointRootsAsync();
            var planned = new List<MediaExportPlanItemViewModel>();
            var destinations = new HashSet<string>(GetPathComparer());
            var sequence = 1;

            foreach (var id in ids)
            {
                var item = await _mediaIndex.GetItemAsync(id);
                if (item is null)
                {
                    planned.Add(Blocked(id, "–", "–", "Medium ist nicht mehr im Media Index vorhanden."));
                    continue;
                }

                var locations = await _mediaIndex.GetLocationsAsync(id);
                var location = locations
                    .Where(location => location.IsPresent && roots.ContainsKey(location.EndpointId))
                    .OrderBy(location => location.EndpointKind == StorageEndpointKind.MediaLibrary ? 0 : 1)
                    .ThenBy(location => location.EndpointId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(location => location.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (location is null || !TryResolveSourcePath(location, roots, out var sourcePath))
                {
                    planned.Add(Blocked(id, "–", "–", "Kein eindeutig erreichbarer aktueller Speicherort gefunden."));
                    continue;
                }

                var sourceInfo = new FileInfo(sourcePath);
                if (!sourceInfo.Exists || (sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    planned.Add(Blocked(id, sourcePath, "–", "Quelle fehlt oder ist ein Reparse-Point/Symlink."));
                    continue;
                }

                if (sourceInfo.Length != item.Length || sourceInfo.Length != location.Length)
                {
                    planned.Add(Blocked(id, sourcePath, "–", "Dateigröße weicht vom indexierten Zustand ab. Medienindex aktualisieren."));
                    continue;
                }

                var metadata = await _mediaIndex.GetMetadataAsync(id);
                var facts = await _mediaIndex.GetFactsAsync(id);
                var knowledge = await _mediaIndex.GetKnowledgeAsync(id);
                var captureDate = metadata.CaptureDateOverridden && metadata.CaptureDate.HasValue
                    ? metadata.CaptureDate.Value
                    : facts?.CaptureDate ?? location.LastWriteTimeUtc.LocalDateTime;

                var namingContext = new MediaNamingContext(
                    captureDate,
                    Path.GetFileNameWithoutExtension(sourcePath),
                    Path.GetExtension(sourcePath),
                    item.MediaType.ToString(),
                    Path.GetFileName(Path.GetDirectoryName(sourcePath) ?? string.Empty),
                    facts?.CameraMake,
                    facts?.CameraModel,
                    metadata.TitleOverridden ? metadata.Title : null,
                    metadata.DescriptionOverridden ? metadata.Description : null,
                    facts?.PixelWidth,
                    facts?.PixelHeight,
                    sequence++);

                string relativeDirectory;
                string stem;
                try
                {
                    relativeDirectory = RenderDirectoryPattern(DirectoryPattern, namingContext);
                    stem = _naming.RenderFileStem(FileNamePattern, namingContext);
                }
                catch (FormatException exception)
                {
                    planned.Add(Blocked(id, sourcePath, "–", exception.Message));
                    continue;
                }

                var exportRequest = selectedExporter is null
                    ? null
                    : CreateImageExportRequest(
                        sourcePath,
                        item.MediaType,
                        facts?.PixelWidth,
                        facts?.PixelHeight);
                var extension = selectedExporter?.FileExtension ?? sourceInfo.Extension;
                if (string.IsNullOrWhiteSpace(extension)) extension = ".bin";
                var destinationRoot = Path.GetFullPath(TargetDirectory);
                var destination = Path.GetFullPath(Path.Combine(destinationRoot, relativeDirectory, stem + extension));
                if (!IsUnderRoot(destinationRoot, destination))
                {
                    planned.Add(Blocked(id, sourcePath, destination, "Die Zielregel würde den gewählten Exportordner verlassen."));
                    continue;
                }

                var relativeDestination = Path.GetRelativePath(destinationRoot, destination);
                var exportMetadata = BuildExportMetadata(metadata, facts, knowledge);
                string? issue = null;
                var effectiveEmbedXmp = EmbedXmp
                    && exportMetadata.HasAnyExportData
                    && (IsJpegExport
                        || (selectedExporter is null && IsJpegExtension(sourceInfo.Extension)));
                var readyStatus = effectiveEmbedXmp
                    ? "Bereit · XMP"
                    : EmbedXmp && !effectiveEmbedXmp
                        ? "Bereit · ohne XMP"
                        : "Bereit";

                if (selectedExporter is not null && exportRequest is not null)
                {
                    var support = selectedExporter.CheckSupport(exportRequest);
                    if (!support.IsSupported)
                    {
                        issue = support.Reason
                            ?? $"{selectedExporter.DisplayName}-Export wird für diese Quelle nicht unterstützt.";
                    }
                }

                if (issue is null
                    && selectedExporter is null
                    && effectiveEmbedXmp
                    && exportMetadata.GpsMode == MetadataGpsMode.Remove
                    && !IsJpegExtension(sourceInfo.Extension))
                {
                    issue = "GPS kann bei bytegenauer Originalkopie nicht aus vorhandenen EXIF-Daten entfernt werden. JPEG-Neuexport verwenden.";
                }

                if (issue is null && PathEquals(sourcePath, destination))
                {
                    issue = "Quelle und Exportziel wären identisch.";
                }
                else if (issue is null && (File.Exists(destination) || Directory.Exists(destination)))
                {
                    issue = "Exportziel existiert bereits. Es wird niemals überschrieben.";
                }
                else if (issue is null && !destinations.Add(destination))
                {
                    issue = "Mehrere Medien würden dasselbe Exportziel erzeugen.";
                }

                if (issue is null && effectiveEmbedXmp && selectedExporter is null)
                {
                    var inspection = await _metadataExport.InspectAsync(sourcePath);
                    if (inspection.EmbeddedXmpExists && !inspection.EmbeddedXmpElbwaldManaged)
                    {
                        issue = "Quelle enthält fremdes eingebettetes XMP. Für verlustfreie Originalkopien wird es nicht überschrieben.";
                    }
                }

                planned.Add(new MediaExportPlanItemViewModel(
                    id,
                    sourcePath,
                    destination,
                    relativeDestination,
                    item.Sha256,
                    item.Length,
                    sourceInfo.LastWriteTimeUtc,
                    item.MediaType,
                    facts?.PixelWidth,
                    facts?.PixelHeight,
                    exportMetadata,
                    effectiveEmbedXmp,
                    issue is null,
                    issue is null ? readyStatus : "Blockiert",
                    issue));
            }

            PlanItems = planned;
            ReadyCount = planned.Count(item => item.IsReady);
            BlockedCount = planned.Count - ReadyCount;
            EstimatedBytes = planned.Where(item => item.IsReady).Sum(item => item.ExpectedLength);
            HasPlan = planned.Count > 0;
            StatusMessage = BlockedCount == 0
                ? $"Exportvorschau bereit: {ReadyCount:N0} Medium/Medien · ca. {EstimatedSizeText}."
                : $"Exportvorschau: {ReadyCount:N0} bereit · {BlockedCount:N0} blockiert. Vor Ausführung müssen alle Konflikte gelöst sein.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or NotSupportedException)
        {
            StatusMessage = "Exportvorschau konnte nicht erstellt werden: " + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExecute() => !IsBusy && HasPlan && ReadyCount > 0 && BlockedCount == 0;

    private async Task ExecuteAsync()
    {
        if (!CanExecute()) return;
        IsBusy = true;
        ResultMessage = string.Empty;
        ProgressPercent = 0;
        _executionCts = new CancellationTokenSource();
        CancelCommand.NotifyCanExecuteChanged();
        var token = _executionCts.Token;
        var batchId = Guid.NewGuid().ToString("N");
        var history = new List<OperationHistoryEntry>();
        var success = 0;
        var failed = 0;

        try
        {
            StatusMessage = "Sicherheitsprüfung vor dem Export …";
            await VerifyAllSourcesAsync(token);
            VerifyTargetLocation();
            VerifyAllDestinations();
            VerifyFreeSpace();

            for (var index = 0; index < PlanItems.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var item = PlanItems[index];
                ProgressText = $"Exportiere {index + 1:N0} von {PlanItems.Count:N0}: {item.SourceFileName}";
                ProgressPercent = PlanItems.Count == 0 ? 0 : index * 100d / PlanItems.Count;

                try
                {
                    var result = await ExportOneAsync(item, token);
                    success++;
                    history.Add(new OperationHistoryEntry(
                        batchId,
                        "ExportCopy",
                        item.SourcePath,
                        item.DestinationPath,
                        result.Sha256,
                        result.Length,
                        item.MediaItemId,
                        DateTimeOffset.UtcNow));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
                {
                    failed++;
                    ResultMessage = $"Export teilweise abgeschlossen: {success:N0} erfolgreich · {failed:N0} fehlgeschlagen. Fehler bei „{item.SourceFileName}“: {exception.GetBaseException().Message}";
                    break;
                }
            }

            var historyWarning = await AppendHistoryAsync(history);

            ProgressPercent = 100;
            if (failed == 0)
            {
                ResultMessage = $"Export abgeschlossen: {success:N0} neue Datei(en) erzeugt. Bibliotheksoriginale wurden nicht verändert.";
            }
            if (!string.IsNullOrWhiteSpace(historyWarning))
            {
                ResultMessage += " " + historyWarning;
            }
            StatusMessage = ResultMessage;
            HasPlan = false;
        }
        catch (OperationCanceledException)
        {
            var historyWarning = await AppendHistoryAsync(history);
            ResultMessage = $"Export abgebrochen. Bereits abgeschlossene Dateien bleiben erhalten ({success:N0}). Bibliotheksoriginale wurden nicht verändert.";
            if (!string.IsNullOrWhiteSpace(historyWarning))
            {
                ResultMessage += " " + historyWarning;
            }
            StatusMessage = ResultMessage;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or CryptographicException)
        {
            ResultMessage = "Export vor dem ersten unsicheren Schreibschritt blockiert: " + exception.GetBaseException().Message;
            StatusMessage = ResultMessage;
            HasPlan = false;
        }
        finally
        {
            _executionCts.Dispose();
            _executionCts = null;
            IsBusy = false;
            ProgressText = string.Empty;
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task<string?> AppendHistoryAsync(IReadOnlyList<OperationHistoryEntry> entries)
    {
        if (entries.Count == 0) return null;
        try
        {
            await _history.AppendBatchAsync(entries, CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return "Hinweis: Die Exportdateien wurden erzeugt, aber der Vorgangsverlauf konnte nicht vollständig geschrieben werden: "
                   + exception.GetBaseException().Message;
        }
    }

    private async Task VerifyAllSourcesAsync(CancellationToken cancellationToken)
    {
        for (var index = 0; index < PlanItems.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = PlanItems[index];
            ProgressText = $"Verifiziere Quelle {index + 1:N0} von {PlanItems.Count:N0} …";
            var info = new FileInfo(item.SourcePath);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Quelle fehlt oder ist nicht sicher lesbar: {item.SourcePath}");
            }
            if (info.Length != item.ExpectedLength || info.LastWriteTimeUtc != item.ExpectedLastWriteTimeUtc)
            {
                throw new IOException($"Quelle wurde seit der Vorschau verändert: {item.SourceFileName}");
            }
            var hash = await HashFileAsync(item.SourcePath, cancellationToken);
            if (!string.Equals(hash, item.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"SHA-256 stimmt nicht mehr mit dem Media Index überein: {item.SourceFileName}");
            }
        }
    }

    private void VerifyTargetLocation()
    {
        if (_plannedTargetLocation is null)
        {
            throw new IOException("Exportziel besitzt keinen freigegebenen stabilen Datenträger-Snapshot. Vorschau neu erstellen.");
        }

        var resolution = _storageResolver.Resolve(_plannedTargetLocation);
        if (!resolution.IsAvailable || string.IsNullOrWhiteSpace(resolution.ResolvedPath))
        {
            throw new IOException("Der für die Vorschau freigegebene Export-Datenträger ist nicht mehr eindeutig verfügbar.");
        }

        if (!PathEquals(resolution.ResolvedPath, TargetDirectory))
        {
            throw new IOException("Exportziel oder Mount-Pfad hat sich seit der Vorschau geändert. Bitte die Vorschau neu erstellen.");
        }
    }

    private void VerifyAllDestinations()
    {
        var seen = new HashSet<string>(GetPathComparer());
        foreach (var item in PlanItems)
        {
            if (!seen.Add(Path.GetFullPath(item.DestinationPath)))
            {
                throw new IOException("Der Exportplan enthält doppelte Zielpfade.");
            }
            if (File.Exists(item.DestinationPath) || Directory.Exists(item.DestinationPath))
            {
                throw new IOException($"Exportziel wurde seit der Vorschau belegt: {item.DestinationPath}");
            }
        }
    }

    private void VerifyFreeSpace()
    {
        if (PlanItems.Count == 0 || string.IsNullOrWhiteSpace(TargetDirectory)) return;
        try
        {
            var fullTarget = Path.GetFullPath(TargetDirectory);
            var drive = DriveInfo.GetDrives()
                .Where(candidate => candidate.IsReady)
                .Select(candidate => new
                {
                    Drive = candidate,
                    Root = Path.GetFullPath(candidate.RootDirectory.FullName)
                })
                .Where(candidate => IsUnderRoot(candidate.Root, fullTarget))
                .OrderByDescending(candidate => candidate.Root.Length)
                .Select(candidate => candidate.Drive)
                .FirstOrDefault();

            if (drive is null) return;
            var required = checked((long)Math.Ceiling(EstimatedBytes * 1.05d));
            if (drive.AvailableFreeSpace < required)
            {
                throw new IOException($"Nicht genügend freier Speicher. Benötigt werden konservativ ca. {FormatBytes(required)}, verfügbar sind {FormatBytes(drive.AvailableFreeSpace)}.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or IOException)
        {
            if (exception is IOException io && io.Message.StartsWith("Nicht genügend freier Speicher", StringComparison.Ordinal))
            {
                throw;
            }
            // Einige virtuelle/Netzwerkziele lassen sich nicht sinnvoll über DriveInfo prüfen.
        }
    }

    private async Task<(string Sha256, long Length)> ExportOneAsync(MediaExportPlanItemViewModel item, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(item.DestinationPath)
            ?? throw new InvalidOperationException("Exportziel hat kein gültiges Verzeichnis.");
        Directory.CreateDirectory(directory);

        var partialBase = item.DestinationPath + "." + Guid.NewGuid().ToString("N");
        string? firstTemp = null;
        string? finalTemp = null;
        try
        {
            if (IsOriginalExport)
            {
                if (item.EmbedXmp)
                {
                    finalTemp = partialBase + ".metadata.jpg";
                    var write = await _metadataExport.WriteEmbeddedCopyAsync(item.SourcePath, finalTemp, item.Metadata, cancellationToken);
                    if (!write.Success || string.IsNullOrWhiteSpace(write.Sha256) || !write.Length.HasValue)
                    {
                        throw new IOException(write.ErrorMessage ?? "XMP konnte nicht in die Exportkopie eingebettet werden.");
                    }
                }
                else
                {
                    finalTemp = partialBase + ".partial";
                    await CopyWithFlushAsync(item.SourcePath, finalTemp, cancellationToken);
                    var copiedHash = await HashFileAsync(finalTemp, cancellationToken);
                    if (!string.Equals(copiedHash, item.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Exportkopie stimmt nicht bytegenau mit der Quelle überein.");
                    }
                }
            }
            else
            {
                var exporter = ResolveSelectedExporter()
                    ?? throw new IOException("Das gewählte Ausgabeformat ist nicht mehr aktiv oder verfügbar.");
                var request = CreateImageExportRequest(
                    item.SourcePath,
                    item.MediaType,
                    item.PixelWidth,
                    item.PixelHeight);
                var support = exporter.CheckSupport(request);
                if (!support.IsSupported)
                {
                    throw new NotSupportedException(
                        support.Reason
                        ?? $"{exporter.DisplayName}-Export wird für diese Quelle nicht unterstützt.");
                }

                firstTemp = partialBase + ".converted" + exporter.FileExtension;
                await using (var output = new FileStream(
                    firstTemp,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.WriteThrough | FileOptions.Asynchronous))
                {
                    await exporter.ExportAsync(request, output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);
                }

                if (item.EmbedXmp)
                {
                    if (!string.Equals(
                            exporter.FormatId,
                            AvaloniaJpegImageExportProvider.JpegFormatId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new NotSupportedException(
                            "XMP-Einbettung ist für das gewählte Exportformat noch nicht verfügbar.");
                    }

                    finalTemp = partialBase + ".metadata.jpg";
                    var write = await _metadataExport.WriteEmbeddedCopyAsync(firstTemp, finalTemp, item.Metadata, cancellationToken);
                    if (!write.Success)
                    {
                        throw new IOException(write.ErrorMessage ?? "XMP konnte nicht in die JPEG-Exportkopie eingebettet werden.");
                    }
                }
                else
                {
                    finalTemp = firstTemp;
                    firstTemp = null;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(finalTemp) || !File.Exists(finalTemp))
            {
                throw new IOException("Temporäre Exportdatei fehlt vor dem Commit.");
            }

            // Source is read-only, but it must still be the exact source approved by the plan.
            // Re-check after producing the temporary output so a concurrent source change never
            // gets silently committed into the export result.
            var sourceAfter = new FileInfo(item.SourcePath);
            if (!sourceAfter.Exists || sourceAfter.Length != item.ExpectedLength || sourceAfter.LastWriteTimeUtc != item.ExpectedLastWriteTimeUtc)
            {
                throw new IOException("Quelle wurde während des Exports verändert.");
            }
            var sourceHashAfter = await HashFileAsync(item.SourcePath, cancellationToken);
            if (!string.Equals(sourceHashAfter, item.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Quelle wurde während des Exports inhaltlich verändert.");
            }

            var outputInfo = new FileInfo(finalTemp);
            if (outputInfo.Length <= 0)
            {
                throw new IOException("Temporäre Exportdatei ist leer.");
            }
            var outputHash = await HashFileAsync(finalTemp, cancellationToken);
            File.Move(finalTemp, item.DestinationPath, overwrite: false);
            finalTemp = null;
            return (outputHash, new FileInfo(item.DestinationPath).Length);
        }
        finally
        {
            TryDelete(firstTemp);
            TryDelete(finalTemp);
        }
    }

    private async Task<IReadOnlyList<string>> GetSourceIdsAsync()
    {
        if (IsSelectionSource)
        {
            return _selectionContext.GetSelection();
        }
        if (SelectedProject is null)
        {
            return Array.Empty<string>();
        }
        var refs = await _projects.GetMediaReferencesAsync(SelectedProject.Id);
        return refs.Select(reference => reference.MediaItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveEndpointRootsAsync()
    {
        var settings = await _storageSettings.LoadAsync();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in settings.Endpoints.Where(endpoint => endpoint.Enabled))
        {
            var resolution = _storageResolver.Resolve(endpoint.Location);
            if (resolution.IsAvailable && !string.IsNullOrWhiteSpace(resolution.ResolvedPath))
            {
                result[endpoint.Id] = Path.GetFullPath(resolution.ResolvedPath);
            }
        }
        return result;
    }

    private static bool TryResolveSourcePath(
        MediaIndexLocation location,
        IReadOnlyDictionary<string, string> roots,
        out string path)
    {
        path = string.Empty;
        if (!roots.TryGetValue(location.EndpointId, out var root)
            || Path.IsPathRooted(location.RelativePath))
        {
            return false;
        }
        try
        {
            var fullRoot = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(Path.Combine(fullRoot, location.RelativePath));
            if (!IsUnderRoot(fullRoot, candidate)) return false;
            path = candidate;
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private MetadataExportValues BuildExportMetadata(
        MediaMetadataSnapshot metadata,
        MediaIndexFacts? facts,
        MediaKnowledgeSnapshot knowledge)
    {
        var effectiveCapture = metadata.CaptureDateOverridden && metadata.CaptureDate.HasValue
            ? metadata.CaptureDate
            : facts?.CaptureDate;

        var gpsMode = metadata.GpsMode;
        var latitude = metadata.Latitude;
        var longitude = metadata.Longitude;
        if (gpsMode == MetadataGpsMode.Inherit && facts?.Latitude.HasValue == true && facts.Longitude.HasValue)
        {
            gpsMode = MetadataGpsMode.Set;
            latitude = facts.Latitude;
            longitude = facts.Longitude;
        }

        string[] Names(MediaKnowledgeKind kind) => knowledge.Items
            .Where(item => item.Kind == kind)
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new MetadataExportValues(
            CaptureDateOverridden: effectiveCapture.HasValue,
            CaptureDate: effectiveCapture,
            TitleOverridden: metadata.TitleOverridden && !string.IsNullOrWhiteSpace(metadata.Title),
            Title: metadata.Title,
            DescriptionOverridden: metadata.DescriptionOverridden && !string.IsNullOrWhiteSpace(metadata.Description),
            Description: metadata.Description,
            GpsMode: gpsMode,
            Latitude: latitude,
            Longitude: longitude,
            Tags: Names(MediaKnowledgeKind.Tag),
            Persons: Names(MediaKnowledgeKind.Person),
            Places: Names(MediaKnowledgeKind.Place),
            Events: Names(MediaKnowledgeKind.Event),
            Rating: Math.Clamp(knowledge.Rating, 0, 3),
            CameraMake: facts?.CameraMake,
            CameraModel: facts?.CameraModel);
    }

    private string RenderDirectoryPattern(string pattern, MediaNamingContext context)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return string.Empty;
        var normalized = pattern.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rendered = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment is "." or "..") throw new FormatException("Zielstruktur darf keine .- oder ..-Segmente enthalten.");
            var value = _naming.RenderFileStem(segment, context);
            if (value is "." or "..") throw new FormatException("Zielstruktur erzeugt ein ungültiges Pfadsegment.");
            rendered.Add(value);
        }
        return rendered.Count == 0 ? string.Empty : Path.Combine(rendered.ToArray());
    }

    private void ValidateDirectoryPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return;
        if (Path.IsPathRooted(pattern)) throw new FormatException("Die Zielstruktur muss relativ zum gewählten Exportordner sein.");
        var normalized = pattern.Replace('\\', '/').Trim('/');
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment is "." or "..") throw new FormatException("Die Zielstruktur darf keine .- oder ..-Segmente enthalten.");
            _naming.Validate(segment);
        }
    }

    private void InsertToken(string? token, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        if (isDirectory)
        {
            DirectoryPattern = string.IsNullOrWhiteSpace(DirectoryPattern)
                ? token
                : DirectoryPattern.TrimEnd('/', '\\') + "/" + token;
        }
        else
        {
            FileNamePattern += token;
        }
    }

    private void ApplyPreset(string? preset)
    {
        switch (preset)
        {
            case "archive":
                DirectoryPattern = "{year}/{month}";
                FileNamePattern = "{date:yyyy-MM-dd}_{original}";
                break;
            case "project":
                DirectoryPattern = "{year}";
                FileNamePattern = "{date:yyyy-MM-dd}_{title}_{seq:000}";
                break;
            case "original":
                DirectoryPattern = string.Empty;
                FileNamePattern = "{original}";
                break;
        }
    }

    private void InvalidatePlan()
    {
        if (_suppressInvalidate) return;
        PlanItems = Array.Empty<MediaExportPlanItemViewModel>();
        _plannedTargetLocation = null;
        HasPlan = false;
        ReadyCount = 0;
        BlockedCount = 0;
        EstimatedBytes = 0;
        ProgressPercent = 0;
        ExecuteCommand.NotifyCanExecuteChanged();
    }

    private void Cancel() => _executionCts?.Cancel();

    private void RefreshFormatOptions()
    {
        FormatOptions = [
            OriginalFormat,
            .. _imageExportRegistry.Providers
                .Select(provider => provider.DisplayName)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
        ];

        if (!FormatOptions.Contains(SelectedFormat, StringComparer.CurrentCultureIgnoreCase))
        {
            SelectedFormat = OriginalFormat;
        }
    }

    private IImageExportProvider? ResolveSelectedExporter()
    {
        if (IsOriginalExport)
        {
            return null;
        }

        return _imageExportRegistry.Providers.FirstOrDefault(
            provider => string.Equals(
                provider.DisplayName,
                SelectedFormat,
                StringComparison.CurrentCultureIgnoreCase));
    }

    private ImageExportRequest CreateImageExportRequest(
        string sourcePath,
        MediaFileType mediaType,
        int? pixelWidth,
        int? pixelHeight)
    {
        return new ImageExportRequest(
            sourcePath,
            mediaType,
            pixelWidth,
            pixelHeight,
            CanResize ? ParseMaxEdge() : null,
            CanSelectQuality ? ExportQuality : null);
    }

    private int ParseMaxEdge()
    {
        if (!CanResize || SelectedMaxEdge.StartsWith("Original", StringComparison.OrdinalIgnoreCase)) return 0;
        var token = SelectedMaxEdge.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(token, out var parsed) ? Math.Clamp(parsed, 320, 12000) : 0;
    }

    private static MediaExportPlanItemViewModel Blocked(string id, string source, string destination, string issue) =>
        new(id, source, destination, destination, string.Empty, 0, DateTime.MinValue, MediaFileType.Unknown, null, null,
            new MetadataExportValues(false, null, false, null, false, null, MetadataGpsMode.Inherit, null, null),
            false, false, "Blockiert", issue);

    private static bool IsJpegExtension(string extension) =>
        string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderRoot(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullCandidate = Path.GetFullPath(candidate);
        if (PathEquals(fullRoot, fullCandidate)) return true;
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(prefix, GetPathComparison());
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), GetPathComparison());

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static async Task CopyWithFlushAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.WriteThrough | FileOptions.Asynchronous);
        await input.CopyToAsync(output, 128 * 1024, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes:N0} B";
        if (bytes < 1024L * 1024L) return $"{bytes / 1024d:N1} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d):N1} MB";
        return $"{bytes / (1024d * 1024d * 1024d):N2} GB";
    }
}
