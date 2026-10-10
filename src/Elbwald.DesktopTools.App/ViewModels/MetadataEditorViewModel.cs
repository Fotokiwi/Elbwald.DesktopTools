using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;
using Elbwald.DesktopTools.Contracts.Media.MetadataExporting;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MetadataEditorViewModel : ObservableObject
{
    private readonly IMediaSourcePicker _sourcePicker;
    private readonly IMediaTypeDetector _mediaTypeDetector;
    private readonly IImageMetadataReader _metadataReader;
    private readonly IMediaIndexService _mediaIndex;
    private readonly IMetadataExportService _metadataExport;
    private readonly IStorageSettingsStore _storageSettings;
    private readonly IStorageLocationResolver _storageResolver;

    [ObservableProperty] private IReadOnlyList<MetadataFileItemViewModel> files = Array.Empty<MetadataFileItemViewModel>();
    [ObservableProperty] private string sourcePath = string.Empty;
    [ObservableProperty] private string statusMessage = "Datei oder Ordner auswählen.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool applyCaptureDate;
    [ObservableProperty] private string captureDateText = string.Empty;
    [ObservableProperty] private bool applyTitle;
    [ObservableProperty] private string titleText = string.Empty;
    [ObservableProperty] private bool applyDescription;
    [ObservableProperty] private string descriptionText = string.Empty;
    [ObservableProperty] private bool applyGps;
    [ObservableProperty] private string latitudeText = string.Empty;
    [ObservableProperty] private string longitudeText = string.Empty;
    [ObservableProperty] private bool removeGps;
    [ObservableProperty] private bool hasPreview;
    [ObservableProperty] private string previewMessage = string.Empty;
    [ObservableProperty] private string resultMessage = string.Empty;
    [ObservableProperty] private bool exportAsSidecar;
    [ObservableProperty] private bool exportEmbedded;
    [ObservableProperty] private bool hasExportPreview;
    [ObservableProperty] private string exportPreviewMessage = string.Empty;
    [ObservableProperty] private string exportResultMessage = string.Empty;

    public MetadataEditorViewModel(
        IMediaSourcePicker sourcePicker,
        IMediaTypeDetector mediaTypeDetector,
        IImageMetadataReader metadataReader,
        IMediaIndexService mediaIndex,
        IMetadataExportService metadataExport,
        IStorageSettingsStore storageSettings,
        IStorageLocationResolver storageResolver)
    {
        _sourcePicker = sourcePicker;
        _mediaTypeDetector = mediaTypeDetector;
        _metadataReader = metadataReader;
        _mediaIndex = mediaIndex;
        _metadataExport = metadataExport;
        _storageSettings = storageSettings;
        _storageResolver = storageResolver;

        ChooseFileCommand = new AsyncRelayCommand(ChooseFileAsync, () => !IsBusy);
        ChooseFolderCommand = new AsyncRelayCommand(ChooseFolderAsync, () => !IsBusy);
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true), () => !IsBusy && Files.Count > 0);
        SelectNoneCommand = new RelayCommand(() => SetAllSelected(false), () => !IsBusy && Files.Count > 0);
        CreatePreviewCommand = new RelayCommand(CreatePreview, CanCreatePreview);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasPreview && !IsBusy);
        CreateExportPreviewCommand = new AsyncRelayCommand(CreateExportPreviewAsync, CanCreateExportPreview);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => HasExportPreview && !IsBusy);
    }

    public IAsyncRelayCommand ChooseFileCommand { get; }
    public IAsyncRelayCommand ChooseFolderCommand { get; }
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand SelectNoneCommand { get; }
    public IRelayCommand CreatePreviewCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand CreateExportPreviewCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }

    public bool HasFiles => Files.Count > 0;
    public int SelectedCount => Files.Count(x => x.IsSelected);
    public string SelectionText => $"{SelectedCount:N0} von {Files.Count:N0} ausgewählt";
    public bool HasResult => !string.IsNullOrWhiteSpace(ResultMessage);

    partial void OnFilesChanged(IReadOnlyList<MetadataFileItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionText));
        SelectAllCommand.NotifyCanExecuteChanged();
        SelectNoneCommand.NotifyCanExecuteChanged();
        CreatePreviewCommand.NotifyCanExecuteChanged();
        CreateExportPreviewCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        ChooseFileCommand.NotifyCanExecuteChanged();
        ChooseFolderCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        SelectNoneCommand.NotifyCanExecuteChanged();
        CreatePreviewCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        CreateExportPreviewCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasPreviewChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnHasExportPreviewChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();
    partial void OnExportAsSidecarChanged(bool value)
    {
        if (value && ExportEmbedded) ExportEmbedded = false;
        InvalidateExportPreview();
    }
    partial void OnExportEmbeddedChanged(bool value)
    {
        if (value && ExportAsSidecar) ExportAsSidecar = false;
        InvalidateExportPreview();
    }
    partial void OnResultMessageChanged(string value) => OnPropertyChanged(nameof(HasResult));
    partial void OnApplyCaptureDateChanged(bool value) => InvalidatePreview();
    partial void OnCaptureDateTextChanged(string value) => InvalidatePreview();
    partial void OnApplyTitleChanged(bool value) => InvalidatePreview();
    partial void OnTitleTextChanged(string value) => InvalidatePreview();
    partial void OnApplyDescriptionChanged(bool value) => InvalidatePreview();
    partial void OnDescriptionTextChanged(string value) => InvalidatePreview();
    partial void OnApplyGpsChanged(bool value)
    {
        if (value && RemoveGps) RemoveGps = false;
        InvalidatePreview();
    }
    partial void OnLatitudeTextChanged(string value) => InvalidatePreview();
    partial void OnLongitudeTextChanged(string value) => InvalidatePreview();
    partial void OnRemoveGpsChanged(bool value)
    {
        if (value && ApplyGps) ApplyGps = false;
        InvalidatePreview();
    }

    private async Task ChooseFileAsync()
    {
        var path = await _sourcePicker.PickFileAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await LoadPathsAsync([path], path);
        }
    }

    private async Task ChooseFolderAsync()
    {
        var folder = await _sourcePicker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder)) return;

        IsBusy = true;
        StatusMessage = "Bilder werden erfasst …";
        try
        {
            var paths = await Task.Run(() => EnumerateImages(folder));
            await LoadPathsCoreAsync(paths, folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Ordner konnte nicht vollständig gelesen werden: " + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadPathsAsync(IReadOnlyList<string> paths, string source)
    {
        IsBusy = true;
        try { await LoadPathsCoreAsync(paths, source); }
        finally { IsBusy = false; }
    }

    private async Task LoadPathsCoreAsync(IReadOnlyList<string> paths, string source)
    {
        SourcePath = source;
        StatusMessage = "Metadaten und Medienidentitäten werden gelesen …";
        ResultMessage = string.Empty;
        HasPreview = false;

        var items = new List<MetadataFileItemViewModel>();
        var unreadable = 0;

        foreach (var path in paths)
        {
            if (_mediaTypeDetector.Detect(path) != MediaFileType.Image) continue;

            try
            {
                var before = new FileInfo(path);
                if (!before.Exists || before.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    unreadable++;
                    continue;
                }

                var hash = await HashStableFileAsync(before.FullName, before.Length, before.LastWriteTimeUtc);
                if (hash is null)
                {
                    unreadable++;
                    continue;
                }

                var mediaItem = await _mediaIndex.EnsureItemAsync(hash, before.Length, MediaFileType.Image);
                var databaseMetadata = await _mediaIndex.GetMetadataAsync(mediaItem.Id);

                var mediaFile = new MediaFile(
                    before.FullName,
                    before.Name,
                    before.Extension,
                    before.Length,
                    before.CreationTimeUtc,
                    before.LastWriteTimeUtc,
                    MediaFileType.Image,
                    false);
                var metadataResult = await _metadataReader.ReadAsync(mediaFile);
                var metadata = metadataResult.Metadata;
                var camera = string.Join(" ", new[] { metadata?.CameraMake, metadata?.CameraModel }.Where(x => !string.IsNullOrWhiteSpace(x)));

                items.Add(new MetadataFileItemViewModel
                {
                    MediaItemId = mediaItem.Id,
                    Sha256 = hash,
                    FullPath = before.FullName,
                    FileName = before.Name,
                    Length = before.Length,
                    LastWriteTimeUtc = before.LastWriteTimeUtc,
                    OriginalCaptureDate = metadata?.CapturedAt,
                    Camera = camera,
                    OriginalLatitude = metadata?.Latitude,
                    OriginalLongitude = metadata?.Longitude,
                    CaptureDateOverridden = databaseMetadata.CaptureDateOverridden,
                    DatabaseCaptureDate = databaseMetadata.CaptureDate,
                    TitleOverridden = databaseMetadata.TitleOverridden,
                    DatabaseTitle = databaseMetadata.Title,
                    DescriptionOverridden = databaseMetadata.DescriptionOverridden,
                    DatabaseDescription = databaseMetadata.Description,
                    GpsMode = databaseMetadata.GpsMode,
                    DatabaseLatitude = databaseMetadata.Latitude,
                    DatabaseLongitude = databaseMetadata.Longitude,
                    Status = databaseMetadata.Exists ? "Elbwald-Metadaten geladen" : metadataResult.IsSuccessful ? "Bereit" : "Metadaten teilweise lesbar"
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                unreadable++;
            }
        }

        Files = items.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var item in Files)
        {
            var inspection = await _metadataExport.InspectAsync(item.FullPath);
            item.ExportStatus = inspection.Summary;
        }
        AttachSelectionTracking();
        StatusMessage = Files.Count == 0
            ? "Keine unterstützten Bilddateien gefunden."
            : $"{Files.Count:N0} Bilddatei(en) geladen" + (unreadable > 0 ? $" · {unreadable:N0} nicht lesbar/übersprungen" : string.Empty) + ".";
    }

    private IReadOnlyList<string> EnumerateImages(string folder)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(folder));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var directoryInfo = new DirectoryInfo(current);
            if (directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"Symlink/Reparse-Verzeichnis wird nicht verfolgt: {current}");

            foreach (var file in directoryInfo.EnumerateFiles())
            {
                if (!file.Attributes.HasFlag(FileAttributes.ReparsePoint) && _mediaTypeDetector.Detect(file.FullName) == MediaFileType.Image)
                    result.Add(file.FullName);
            }

            foreach (var child in directoryInfo.EnumerateDirectories())
            {
                if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException($"Symlink/Reparse-Verzeichnis wird nicht verfolgt: {child.FullName}");
                pending.Push(child.FullName);
            }
        }
        return result;
    }

    private void AttachSelectionTracking()
    {
        foreach (var item in Files)
        {
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MetadataFileItemViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(SelectionText));
                    InvalidatePreview();
                    InvalidateExportPreview();
                }
            };
        }
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var item in Files) item.IsSelected = selected;
        InvalidatePreview();
        InvalidateExportPreview();
    }

    private bool CanCreatePreview() => !IsBusy && Files.Any(x => x.IsSelected) && HasAnyEdit();
    private bool HasAnyEdit() => ApplyCaptureDate || ApplyTitle || ApplyDescription || ApplyGps || RemoveGps;

    private void CreatePreview()
    {
        var selected = Files.Where(x => x.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            PreviewMessage = "Keine Dateien ausgewählt.";
            return;
        }

        if (!TryBuildValues(out _, out var validationError))
        {
            HasPreview = false;
            PreviewMessage = validationError;
            return;
        }

        var changes = new List<string>();
        if (ApplyCaptureDate) changes.Add("Aufnahmedatum");
        if (ApplyTitle) changes.Add("Titel");
        if (ApplyDescription) changes.Add("Beschreibung");
        if (RemoveGps) changes.Add("GPS entfernen");
        else if (ApplyGps) changes.Add("GPS setzen");

        HasPreview = true;
        PreviewMessage = $"Vorschau: {selected.Length:N0} Datei(en) · {string.Join(", ", changes)}. Gespeichert wird ausschließlich zentral in media-index.db; Mediendateien und Sidecars bleiben unverändert.";
    }

    private async Task SaveAsync()
    {
        if (!HasPreview)
        {
            PreviewMessage = "Bitte zuerst eine Vorschau der Metadatenänderungen erstellen.";
            return;
        }

        if (!TryBuildValues(out var entered, out var validationError))
        {
            PreviewMessage = validationError;
            HasPreview = false;
            return;
        }

        IsBusy = true;
        ResultMessage = string.Empty;
        try
        {
            var selected = Files.Where(x => x.IsSelected).ToArray();
            var updates = new List<MediaMetadataUpdate>(selected.Length);

            // Vollständiger Preflight vor der einzigen DB-Transaktion.
            foreach (var item in selected)
            {
                var info = new FileInfo(item.FullPath);
                if (!info.Exists || info.Length != item.Length || info.LastWriteTimeUtc != item.LastWriteTimeUtc)
                {
                    ResultMessage = $"Abgebrochen: '{item.FileName}' wurde seit dem Laden verändert. Es wurden keine Metadaten gespeichert.";
                    return;
                }

                var currentHash = await HashStableFileAsync(item.FullPath, item.Length, item.LastWriteTimeUtc);
                if (!string.Equals(currentHash, item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    ResultMessage = $"Abgebrochen: Inhalt von '{item.FileName}' stimmt nicht mehr mit der geladenen Medienidentität überein. Es wurden keine Metadaten gespeichert.";
                    return;
                }

                var currentIndexItem = await _mediaIndex.GetItemAsync(item.MediaItemId);
                if (currentIndexItem is null
                    || currentIndexItem.Length != item.Length
                    || !string.Equals(currentIndexItem.Sha256, item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    ResultMessage = $"Abgebrochen: Media-ID von '{item.FileName}' ist nicht mehr eindeutig. Es wurden keine Metadaten gespeichert.";
                    return;
                }

                updates.Add(MergeValuesForItem(item, entered!));
            }

            await _mediaIndex.SaveMetadataBatchAsync(updates);

            foreach (var item in selected)
            {
                var snapshot = await _mediaIndex.GetMetadataAsync(item.MediaItemId);
                item.ApplyDatabaseSnapshot(snapshot);
                item.Status = "In Elbwald gespeichert";
            }

            ResultMessage = $"{selected.Length:N0} Medien-Metadatensatz/-sätze atomar in media-index.db gespeichert. Originaldateien wurden nicht verändert; es wurden keine XMP-Dateien erzeugt.";
            HasPreview = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ResultMessage = "Speichern fehlgeschlagen. Die DB-Transaktion wurde nicht erfolgreich abgeschlossen: " + exception.GetBaseException().Message;
            HasPreview = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private MediaMetadataUpdate MergeValuesForItem(MetadataFileItemViewModel item, MetadataEditValues entered)
    {
        var captureOverridden = ApplyCaptureDate || item.CaptureDateOverridden;
        var captureDate = ApplyCaptureDate ? entered.CaptureDate : item.DatabaseCaptureDate;
        var titleOverridden = ApplyTitle || item.TitleOverridden;
        var title = ApplyTitle ? entered.Title : item.DatabaseTitle;
        var descriptionOverridden = ApplyDescription || item.DescriptionOverridden;
        var description = ApplyDescription ? entered.Description : item.DatabaseDescription;

        var gpsMode = item.GpsMode;
        var latitude = item.DatabaseLatitude;
        var longitude = item.DatabaseLongitude;
        if (RemoveGps)
        {
            gpsMode = MetadataGpsMode.Remove;
            latitude = null;
            longitude = null;
        }
        else if (ApplyGps)
        {
            gpsMode = MetadataGpsMode.Set;
            latitude = entered.Latitude;
            longitude = entered.Longitude;
        }

        return new MediaMetadataUpdate(
            item.MediaItemId,
            captureOverridden,
            captureDate,
            titleOverridden,
            title,
            descriptionOverridden,
            description,
            gpsMode,
            latitude,
            longitude);
    }

    private bool TryBuildValues(out MetadataEditValues? values, out string error)
    {
        values = null;
        error = string.Empty;
        DateTime? captureDate = null;
        if (ApplyCaptureDate)
        {
            if (!TryParseDate(CaptureDateText, out var parsedDate))
            {
                error = "Aufnahmedatum bitte als YYYY-MM-DD HH:MM:SS eingeben.";
                return false;
            }
            captureDate = parsedDate;
        }

        double? latitude = null;
        double? longitude = null;
        if (ApplyGps && !RemoveGps)
        {
            if (!TryParseCoordinate(LatitudeText, -90, 90, out var lat)
                || !TryParseCoordinate(LongitudeText, -180, 180, out var lon))
            {
                error = "GPS ungültig. Breite muss -90…90, Länge -180…180 sein.";
                return false;
            }
            latitude = lat;
            longitude = lon;
        }

        values = new MetadataEditValues(
            captureDate,
            ApplyTitle ? TitleText : null,
            ApplyDescription ? DescriptionText : null,
            latitude,
            longitude,
            RemoveGps);
        return true;
    }

    private static async Task<string?> HashStableFileAsync(string path, long expectedLength, DateTime expectedLastWriteUtc)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream);
            var after = new FileInfo(path);
            if (!after.Exists || after.Length != expectedLength || after.LastWriteTimeUtc != expectedLastWriteUtc)
                return null;
            return Convert.ToHexString(hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryParseDate(string value, out DateTime parsed)
    {
        var formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd" };
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
    }

    private static bool TryParseCoordinate(string value, double min, double max, out double parsed)
    {
        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)
            && !double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            return false;
        return parsed >= min && parsed <= max;
    }

    private bool CanCreateExportPreview() =>
        !IsBusy && Files.Any(x => x.IsSelected) && (ExportAsSidecar || ExportEmbedded);

    private async Task CreateExportPreviewAsync()
    {
        HasExportPreview = false;
        ExportResultMessage = string.Empty;
        var selected = Files.Where(x => x.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            ExportPreviewMessage = "Keine Dateien ausgewählt.";
            return;
        }

        var blocked = 0;
        var exportable = 0;
        foreach (var item in selected)
        {
            var values = BuildExportValues(item);
            if (!values.HasAnyOverride)
            {
                blocked++;
                continue;
            }

            var inspection = await _metadataExport.InspectAsync(item.FullPath);
            if (ExportAsSidecar && inspection.SidecarExists && !inspection.SidecarElbwaldManaged)
            {
                blocked++;
                continue;
            }

            if (ExportEmbedded)
            {
                var capability = _metadataExport.GetEmbeddedCapability(item.FullPath, values);
                if (!capability.Supported
                    || !await IsUniqueCurrentIndexedPathAsync(item)
                    || inspection.EmbeddedXmpExists && !inspection.EmbeddedXmpElbwaldManaged)
                {
                    blocked++;
                    continue;
                }
            }
            exportable++;
        }

        HasExportPreview = blocked == 0 && exportable > 0;
        ExportPreviewMessage = ExportAsSidecar
            ? $"XMP-Sidecar: {exportable:N0} bereit" + (blocked > 0 ? $" · {blocked:N0} blockiert" : string.Empty) + ". Fremde Sidecars werden beim Schreiben erneut geprüft."
            : $"JPEG-XMP einbetten: {exportable:N0} bereit" + (blocked > 0 ? $" · {blocked:N0} blockiert" : string.Empty) + ". Nur eindeutig indexierte Einzelkopien werden verändert.";
    }

    private async Task ExportAsync()
    {
        if (!HasExportPreview) return;
        IsBusy = true;
        ExportResultMessage = string.Empty;
        var selected = Files.Where(x => x.IsSelected).ToArray();
        var success = 0;
        var failed = 0;
        var recoveryRequired = 0;
        var batchId = Guid.NewGuid().ToString("N");
        try
        {
            // Vollständiger Export-Preflight vor dem ersten Schreibzugriff.
            foreach (var item in selected)
            {
                var values = BuildExportValues(item);
                if (!values.HasAnyOverride)
                {
                    ExportResultMessage = $"Export abgebrochen: '{item.FileName}' besitzt keine Elbwald-Overrides.";
                    HasExportPreview = false;
                    return;
                }

                var info = new FileInfo(item.FullPath);
                if (!info.Exists || info.Length != item.Length || info.LastWriteTimeUtc != item.LastWriteTimeUtc)
                {
                    ExportResultMessage = $"Export abgebrochen: '{item.FileName}' wurde seit der Vorschau verändert.";
                    HasExportPreview = false;
                    return;
                }

                var currentHash = await HashStableFileAsync(item.FullPath, item.Length, item.LastWriteTimeUtc);
                if (!string.Equals(currentHash, item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    ExportResultMessage = $"Export abgebrochen: SHA-256 von '{item.FileName}' stimmt nicht mehr.";
                    HasExportPreview = false;
                    return;
                }

                var inspection = await _metadataExport.InspectAsync(item.FullPath);
                if (ExportAsSidecar && inspection.SidecarExists && !inspection.SidecarElbwaldManaged)
                {
                    ExportResultMessage = $"Export abgebrochen: '{item.FileName}.xmp' ist keine eindeutig von Elbwald verwaltete Sidecar.";
                    HasExportPreview = false;
                    return;
                }

                if (ExportEmbedded)
                {
                    var capability = _metadataExport.GetEmbeddedCapability(item.FullPath, values);
                    if (!capability.Supported || !await IsUniqueCurrentIndexedPathAsync(item)
                        || inspection.EmbeddedXmpExists && !inspection.EmbeddedXmpElbwaldManaged)
                    {
                        ExportResultMessage = $"Export abgebrochen: '{item.FileName}' ist für den direkten JPEG-Export nicht eindeutig/sicher freigegeben. {capability.Message}";
                        HasExportPreview = false;
                        return;
                    }
                }
            }

            foreach (var item in selected)
            {
                var values = BuildExportValues(item);
                if (!values.HasAnyOverride) { failed++; continue; }

                if (ExportAsSidecar)
                {
                    var result = await _metadataExport.WriteSidecarAsync(item.FullPath, values);
                    if (result.Success)
                    {
                        success++;
                        item.ExportStatus = "XMP-Sidecar exportiert";
                    }
                    else
                    {
                        failed++;
                        item.Status = "Export blockiert: " + result.ErrorMessage;
                    }
                    continue;
                }

                if (!await IsUniqueCurrentIndexedPathAsync(item))
                {
                    failed++;
                    item.Status = "Direktexport blockiert: Speicherort nicht eindeutig.";
                    continue;
                }

                var write = await _metadataExport.WriteEmbeddedAsync(
                    item.MediaItemId,
                    item.FullPath,
                    item.Sha256,
                    item.Length,
                    item.LastWriteTimeUtc,
                    values,
                    batchId);
                if (!write.Success || string.IsNullOrWhiteSpace(write.NewSha256)
                    || !write.NewLength.HasValue || !write.NewLastWriteTimeUtc.HasValue)
                {
                    failed++;
                    if (write.RecoveryRequired) recoveryRequired++;
                    item.Status = "Direktexport fehlgeschlagen: " + write.ErrorMessage;
                    continue;
                }

                try
                {
                    await _mediaIndex.UpdateIdentityAfterControlledRewriteAsync(
                        item.MediaItemId,
                        item.Sha256,
                        item.Length,
                        write.NewSha256,
                        write.NewLength.Value,
                        write.NewLastWriteTimeUtc.Value);
                    item.ApplyPhysicalSnapshot(write.NewSha256, write.NewLength.Value, write.NewLastWriteTimeUtc.Value);
                    item.ExportStatus = "JPEG-XMP eingebettet";
                    item.Status = "Exportiert und Media-ID erhalten";
                    _metadataExport.FinalizeBackup(write.BackupPath);
                    success++;
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    var restored = !string.IsNullOrWhiteSpace(write.BackupPath)
                                   && await _metadataExport.RestoreEmbeddedAsync(item.FullPath, write.BackupPath, write.NewSha256);
                    failed++;
                    if (!restored) recoveryRequired++;
                    item.Status = restored
                        ? "Index-Update fehlgeschlagen; Originaldatei wurde wiederhergestellt."
                        : "Index-Update fehlgeschlagen; Recovery erforderlich: " + exception.GetBaseException().Message;
                }
            }

            ExportResultMessage = $"Export abgeschlossen: {success:N0} erfolgreich · {failed:N0} fehlgeschlagen"
                                  + (recoveryRequired > 0 ? $" · {recoveryRequired:N0} Recovery erforderlich" : string.Empty) + ".";
            HasExportPreview = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private MetadataExportValues BuildExportValues(MetadataFileItemViewModel item) =>
        new(
            item.CaptureDateOverridden,
            item.DatabaseCaptureDate,
            item.TitleOverridden,
            item.DatabaseTitle,
            item.DescriptionOverridden,
            item.DatabaseDescription,
            item.GpsMode,
            item.DatabaseLatitude,
            item.DatabaseLongitude);

    private async Task<bool> IsUniqueCurrentIndexedPathAsync(MetadataFileItemViewModel item)
    {
        var locations = (await _mediaIndex.GetLocationsAsync(item.MediaItemId)).Where(x => x.IsPresent).ToArray();
        if (locations.Length != 1) return false;
        var settings = await _storageSettings.LoadAsync();
        var endpoint = settings.Find(locations[0].EndpointId);
        if (endpoint is null) return false;
        var resolution = _storageResolver.Resolve(endpoint.Location);
        if (!resolution.IsAvailable || string.IsNullOrWhiteSpace(resolution.ResolvedPath)) return false;
        var indexedPath = Path.GetFullPath(Path.Combine(resolution.ResolvedPath, locations[0].RelativePath));
        var selectedPath = Path.GetFullPath(item.FullPath);
        return string.Equals(indexedPath, selectedPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private void InvalidateExportPreview()
    {
        HasExportPreview = false;
        ExportPreviewMessage = string.Empty;
        ExportResultMessage = string.Empty;
        CreateExportPreviewCommand.NotifyCanExecuteChanged();
    }

    private void InvalidatePreview()
    {
        HasPreview = false;
        PreviewMessage = string.Empty;
        ResultMessage = string.Empty;
        CreatePreviewCommand.NotifyCanExecuteChanged();
    }
}
