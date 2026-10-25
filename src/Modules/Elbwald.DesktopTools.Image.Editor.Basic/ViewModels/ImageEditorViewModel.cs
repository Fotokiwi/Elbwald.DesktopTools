using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Editing;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Image.Editor.Basic.Operations;
using Elbwald.DesktopTools.Image.Editor.Basic.Presentation;
using Elbwald.DesktopTools.Image.Editor.Basic.Preview;

namespace Elbwald.DesktopTools.Image.Editor.Basic.ViewModels;

public sealed class ImageEditorViewModel : ObservableObject, IDisposable
{
    private const int RecentImageLimit = 80;
    private const int DefaultPreviewMaxEdge = 2048;
    private const double MinimumZoomPercent = 25;
    private const double MaximumZoomPercent = 400;
    private const double ZoomStepPercent = 25;
    private const int TonePreviewDebounceMilliseconds = 90;

    private readonly IMediaIndexService _mediaIndexService;
    private readonly ImageEditPreviewService _previewService;
    private readonly IImageEditDocumentStore _documentStore;
    private readonly Stack<ImageEditDocument> _undoStack = new();
    private readonly Stack<ImageEditDocument> _redoStack = new();

    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _tonePreviewCancellation;
    private long _tonePreviewGeneration;
    private ImageEditDocument? _workingDocument;
    private ImageEditDocument? _savedDocument;
    private bool _isInitialized;
    private bool _isBusy;
    private bool _hasImage;
    private bool _hasError;
    private bool _showOriginal;
    private bool _isFitMode = true;
    private bool _hasEnabledEdits;
    private bool _hasUnsavedChanges;
    private bool _isCropMode;
    private double _cropLeft;
    private double _cropTop;
    private double _cropWidth = 1d;
    private double _cropHeight = 1d;
    private double _exposureEv;
    private double _contrast;
    private double _saturation;
    private double _committedExposureEv;
    private double _committedContrast;
    private double _committedSaturation;
    private string? _editableToneOperationId;
    private bool _suppressTonePreview;
    private double _zoomPercent = 100;
    private string _statusMessage =
        "Wähle ein Bild aus dem Medienindex. Das Original bleibt unverändert.";
    private string _errorMessage = string.Empty;
    private string _currentFileName = "Kein Bild geöffnet";
    private string _currentFileDetails = "";
    private ImageEditorMediaItemViewModel? _selectedMediaItem;
    private Bitmap? _sourceBitmap;
    private Bitmap? _renderedBitmap;
    private Bitmap? _displayBitmap;
    private int _sourcePixelWidth;
    private int _sourcePixelHeight;
    private int _renderedPixelWidth;
    private int _renderedPixelHeight;

    public ImageEditorViewModel(
        IMediaIndexService mediaIndexService,
        ImageEditPreviewService previewService,
        IImageEditDocumentStore documentStore)
    {
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        ArgumentNullException.ThrowIfNull(previewService);
        ArgumentNullException.ThrowIfNull(documentStore);

        _mediaIndexService = mediaIndexService;
        _previewService = previewService;
        _documentStore = documentStore;

        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        RefreshMediaCommand = new AsyncRelayCommand(RefreshMediaAsync, () => !IsBusy);
        OpenSelectedCommand = new AsyncRelayCommand(
            OpenSelectedAsync,
            () => !IsBusy && SelectedMediaItem is not null && !HasUnsavedChanges && !IsCropMode && !HasToneDraft);

        FitCommand = new RelayCommand(
            () => IsFitMode = true,
            () => HasImage && !IsBusy && !IsCropMode);

        ActualSizeCommand = new RelayCommand(
            () =>
            {
                ZoomPercent = 100;
                IsFitMode = false;
            },
            () => HasImage && !IsBusy && !IsCropMode);

        ZoomInCommand = new RelayCommand(
            () =>
            {
                IsFitMode = false;
                ZoomPercent = Math.Min(MaximumZoomPercent, ZoomPercent + ZoomStepPercent);
            },
            () => HasImage && !IsBusy && !IsCropMode && ZoomPercent < MaximumZoomPercent);

        ZoomOutCommand = new RelayCommand(
            () =>
            {
                IsFitMode = false;
                ZoomPercent = Math.Max(MinimumZoomPercent, ZoomPercent - ZoomStepPercent);
            },
            () => HasImage && !IsBusy && !IsCropMode && ZoomPercent > MinimumZoomPercent);

        ToggleBeforeAfterCommand = new RelayCommand(
            () => ShowOriginal = !ShowOriginal,
            () => HasImage && !IsBusy && !IsCropMode);

        RotateCounterClockwiseCommand = new AsyncRelayCommand(
            () => ApplyOperationAsync(
                BasicImageEditOperations.RotateClockwise(3),
                "90° gegen den Uhrzeigersinn gedreht"),
            CanEdit);

        RotateClockwiseCommand = new AsyncRelayCommand(
            () => ApplyOperationAsync(
                BasicImageEditOperations.RotateClockwise(),
                "90° im Uhrzeigersinn gedreht"),
            CanEdit);

        FlipHorizontalCommand = new AsyncRelayCommand(
            () => ApplyOperationAsync(
                BasicImageEditOperations.FlipHorizontal(),
                "Horizontal gespiegelt"),
            CanEdit);

        FlipVerticalCommand = new AsyncRelayCommand(
            () => ApplyOperationAsync(
                BasicImageEditOperations.FlipVertical(),
                "Vertikal gespiegelt"),
            CanEdit);

        StartCropCommand = new RelayCommand(StartCrop, CanStartCrop);
        ApplyCropCommand = new AsyncRelayCommand(ApplyCropAsync, CanApplyCrop);
        CancelCropCommand = new RelayCommand(CancelCrop, CanCancelCrop);

        ApplyToneCommand = new AsyncRelayCommand(ApplyToneAsync, CanApplyTone);
        ResetToneCommand = new RelayCommand(ResetToneDraft, CanResetTone);

        UndoCommand = new AsyncRelayCommand(UndoAsync, CanUndo);
        RedoCommand = new AsyncRelayCommand(RedoAsync, CanRedo);
        SaveEditsCommand = new AsyncRelayCommand(SaveEditsAsync, CanSaveEdits);
    }

    public ObservableCollection<ImageEditorMediaItemViewModel> RecentImages { get; } = [];

    public IAsyncRelayCommand InitializeCommand { get; }

    public IAsyncRelayCommand RefreshMediaCommand { get; }

    public IAsyncRelayCommand OpenSelectedCommand { get; }

    public IRelayCommand FitCommand { get; }

    public IRelayCommand ActualSizeCommand { get; }

    public IRelayCommand ZoomInCommand { get; }

    public IRelayCommand ZoomOutCommand { get; }

    public IRelayCommand ToggleBeforeAfterCommand { get; }

    public IAsyncRelayCommand RotateCounterClockwiseCommand { get; }

    public IAsyncRelayCommand RotateClockwiseCommand { get; }

    public IAsyncRelayCommand FlipHorizontalCommand { get; }

    public IAsyncRelayCommand FlipVerticalCommand { get; }

    public IRelayCommand StartCropCommand { get; }

    public IAsyncRelayCommand ApplyCropCommand { get; }

    public IRelayCommand CancelCropCommand { get; }

    public IAsyncRelayCommand ApplyToneCommand { get; }

    public IRelayCommand ResetToneCommand { get; }

    public IAsyncRelayCommand UndoCommand { get; }

    public IAsyncRelayCommand RedoCommand { get; }

    public IAsyncRelayCommand SaveEditsCommand { get; }

    public ImageEditorMediaItemViewModel? SelectedMediaItem
    {
        get => _selectedMediaItem;
        set
        {
            if (!SetProperty(ref _selectedMediaItem, value))
            {
                return;
            }

            OpenSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            RefreshMediaCommand.NotifyCanExecuteChanged();
            OpenSelectedCommand.NotifyCanExecuteChanged();
            FitCommand.NotifyCanExecuteChanged();
            ActualSizeCommand.NotifyCanExecuteChanged();
            ZoomInCommand.NotifyCanExecuteChanged();
            ZoomOutCommand.NotifyCanExecuteChanged();
            ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
            RefreshEditorCommandStates();
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(AreToneControlsEnabled));
        }
    }

    public bool HasImage
    {
        get => _hasImage;
        private set
        {
            if (!SetProperty(ref _hasImage, value))
            {
                return;
            }

            FitCommand.NotifyCanExecuteChanged();
            ActualSizeCommand.NotifyCanExecuteChanged();
            ZoomInCommand.NotifyCanExecuteChanged();
            ZoomOutCommand.NotifyCanExecuteChanged();
            ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
            RefreshEditorCommandStates();
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(IsEditorVisible));
            OnPropertyChanged(nameof(AreToneControlsEnabled));
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (!SetProperty(ref _hasError, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsEmptyStateVisible));
        }
    }

    public bool IsEmptyStateVisible => !IsBusy && !HasImage;

    public bool IsEditorVisible => HasImage;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string CurrentFileName
    {
        get => _currentFileName;
        private set => SetProperty(ref _currentFileName, value);
    }

    public string CurrentFileDetails
    {
        get => _currentFileDetails;
        private set => SetProperty(ref _currentFileDetails, value);
    }

    public bool HasEnabledEdits
    {
        get => _hasEnabledEdits;
        private set => SetProperty(ref _hasEnabledEdits, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (!SetProperty(ref _hasUnsavedChanges, value))
            {
                return;
            }

            OpenSelectedCommand.NotifyCanExecuteChanged();
            SaveEditsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(EditStateLabel));
        }
    }

    public string EditStateLabel
    {
        get
        {
            var operationCount = _workingDocument?.Operations.Count(operation => operation.IsEnabled) ?? 0;
            var stepLabel = operationCount == 1 ? "1 Schritt" : $"{operationCount} Schritte";

            if (HasUnsavedChanges)
            {
                return $"{stepLabel} · nicht gespeichert";
            }

            return operationCount == 0
                ? "Keine Bearbeitungsschritte"
                : $"{stepLabel} · gespeichert";
        }
    }


    public bool IsCropMode
    {
        get => _isCropMode;
        private set
        {
            if (!SetProperty(ref _isCropMode, value))
            {
                return;
            }

            OpenSelectedCommand.NotifyCanExecuteChanged();
            FitCommand.NotifyCanExecuteChanged();
            ActualSizeCommand.NotifyCanExecuteChanged();
            ZoomInCommand.NotifyCanExecuteChanged();
            ZoomOutCommand.NotifyCanExecuteChanged();
            ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
            RefreshEditorCommandStates();
            OnPropertyChanged(nameof(CropSelectionLabel));
            OnPropertyChanged(nameof(IsCropInactive));
            OnPropertyChanged(nameof(AreToneControlsEnabled));
        }
    }

    public bool IsCropInactive => !IsCropMode;

    public bool AreToneControlsEnabled => HasImage && !IsBusy && !IsCropMode;

    public double CropLeft
    {
        get => _cropLeft;
        private set
        {
            if (SetProperty(ref _cropLeft, value))
            {
                OnPropertyChanged(nameof(CropSelectionLabel));
            }
        }
    }

    public double CropTop
    {
        get => _cropTop;
        private set
        {
            if (SetProperty(ref _cropTop, value))
            {
                OnPropertyChanged(nameof(CropSelectionLabel));
            }
        }
    }

    public double CropWidth
    {
        get => _cropWidth;
        private set
        {
            if (SetProperty(ref _cropWidth, value))
            {
                OnPropertyChanged(nameof(CropSelectionLabel));
            }
        }
    }

    public double CropHeight
    {
        get => _cropHeight;
        private set
        {
            if (SetProperty(ref _cropHeight, value))
            {
                OnPropertyChanged(nameof(CropSelectionLabel));
            }
        }
    }

    public int RenderedPixelWidth => _renderedPixelWidth;

    public int RenderedPixelHeight => _renderedPixelHeight;

    public string CropSelectionLabel
    {
        get
        {
            if (!IsCropMode || _renderedPixelWidth <= 0 || _renderedPixelHeight <= 0)
            {
                return "";
            }

            var pixelWidth = Math.Max(1, (int)Math.Round(CropWidth * _renderedPixelWidth));
            var pixelHeight = Math.Max(1, (int)Math.Round(CropHeight * _renderedPixelHeight));
            return $"{pixelWidth} × {pixelHeight} px";
        }
    }

    public double ExposureEv
    {
        get => _exposureEv;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, -3d, 3d), 1);
            if (!SetProperty(ref _exposureEv, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(ExposureLabel));
            OnToneControlChanged();
        }
    }

    public double Contrast
    {
        get => _contrast;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, -100d, 100d));
            if (!SetProperty(ref _contrast, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(ContrastLabel));
            OnToneControlChanged();
        }
    }

    public double Saturation
    {
        get => _saturation;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, -100d, 100d));
            if (!SetProperty(ref _saturation, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(SaturationLabel));
            OnToneControlChanged();
        }
    }

    public string ExposureLabel => $"{ExposureEv:+0.0;-0.0;0.0} EV";

    public string ContrastLabel => $"{Contrast:+0;-0;0}";

    public string SaturationLabel => $"{Saturation:+0;-0;0}";

    public bool HasToneDraft => !AreToneValuesEqual(
        ExposureEv,
        Contrast,
        Saturation,
        _committedExposureEv,
        _committedContrast,
        _committedSaturation);

    public bool HasToneAdjustment => !IsToneNeutral(ExposureEv, Contrast, Saturation);

    public bool ShowOriginal
    {
        get => _showOriginal;
        set
        {
            if (!SetProperty(ref _showOriginal, value))
            {
                return;
            }

            UpdateDisplayBitmap();
            OnPropertyChanged(nameof(BeforeAfterLabel));
            OnPropertyChanged(nameof(ZoomedWidth));
            OnPropertyChanged(nameof(ZoomedHeight));
        }
    }

    public string BeforeAfterLabel => ShowOriginal ? "Original" : "Bearbeitet";

    public bool IsFitMode
    {
        get => _isFitMode;
        private set
        {
            if (!SetProperty(ref _isFitMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsZoomMode));
            OnPropertyChanged(nameof(ZoomLabel));
        }
    }

    public bool IsZoomMode => !IsFitMode;

    public double ZoomPercent
    {
        get => _zoomPercent;
        private set
        {
            var clamped = Math.Clamp(value, MinimumZoomPercent, MaximumZoomPercent);
            if (!SetProperty(ref _zoomPercent, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(ZoomLabel));
            OnPropertyChanged(nameof(ZoomedWidth));
            OnPropertyChanged(nameof(ZoomedHeight));
            ZoomInCommand.NotifyCanExecuteChanged();
            ZoomOutCommand.NotifyCanExecuteChanged();
        }
    }

    public string ZoomLabel => IsFitMode ? "Einpassen" : $"{ZoomPercent:0} %";

    public double ZoomedWidth => (ShowOriginal ? _sourcePixelWidth : _renderedPixelWidth) * ZoomPercent / 100d;

    public double ZoomedHeight => (ShowOriginal ? _sourcePixelHeight : _renderedPixelHeight) * ZoomPercent / 100d;

    public Bitmap? DisplayBitmap
    {
        get => _displayBitmap;
        private set => SetProperty(ref _displayBitmap, value);
    }

    private bool CanModifyDocument()
    {
        return HasImage && !IsBusy && _workingDocument is not null;
    }

    private bool CanEdit()
    {
        return CanModifyDocument() && !IsCropMode && !HasToneDraft;
    }

    private bool CanStartCrop()
    {
        return CanEdit();
    }

    private bool CanApplyCrop()
    {
        return CanModifyDocument() && IsCropMode;
    }

    private bool CanCancelCrop()
    {
        return IsCropMode && !IsBusy;
    }

    private bool CanApplyTone()
    {
        return CanModifyDocument() && !IsCropMode && HasToneDraft;
    }

    private bool CanResetTone()
    {
        return CanModifyDocument() && !IsCropMode && HasToneAdjustment;
    }

    private bool CanUndo()
    {
        return CanEdit() && _undoStack.Count > 0;
    }

    private bool CanRedo()
    {
        return CanEdit() && _redoStack.Count > 0;
    }

    private bool CanSaveEdits()
    {
        return CanEdit() && HasUnsavedChanges;
    }

    private async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await RefreshMediaAsync();
    }

    private async Task RefreshMediaAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = "Bilder aus dem Medienindex werden geladen …";

        try
        {
            var results = await _mediaIndexService.SearchAsync(
                new MediaIndexSearchQuery(
                    PresentOnly: true,
                    Limit: RecentImageLimit,
                    MediaType: MediaFileType.Image,
                    SortOrder: MediaIndexSortOrder.CaptureDateDescending));

            RecentImages.Clear();
            foreach (var result in results)
            {
                RecentImages.Add(new ImageEditorMediaItemViewModel(result));
            }

            SelectedMediaItem = RecentImages.FirstOrDefault();
            StatusMessage = RecentImages.Count == 0
                ? "Im Medienindex sind aktuell keine vorhandenen Bilder verfügbar."
                : $"{RecentImages.Count} Bilder stehen zur Auswahl bereit.";
        }
        catch (Exception exception)
        {
            HasError = true;
            ErrorMessage = exception.GetBaseException().Message;
            StatusMessage = "Der Medienindex konnte nicht geladen werden.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenSelectedAsync()
    {
        var selected = SelectedMediaItem;
        if (selected is null || IsBusy || HasUnsavedChanges || IsCropMode || HasToneDraft)
        {
            return;
        }

        CancelTonePreview();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = $"{selected.DisplayName} wird für die Vorschau geladen …";

        try
        {
            var result = await _previewService.RenderAsync(
                new ImageEditPreviewRequest(
                    selected.MediaItemId,
                    DefaultPreviewMaxEdge),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var sourceBitmap = DecodedImageFrameBitmapFactory.Create(result.SourceFrame);
            var renderedBitmap = DecodedImageFrameBitmapFactory.Create(result.RenderedFrame);

            ReplaceBitmaps(sourceBitmap, renderedBitmap);
            _sourcePixelWidth = result.SourceFrame.PixelWidth;
            _sourcePixelHeight = result.SourceFrame.PixelHeight;
            UpdateRenderedDimensions(result.RenderedFrame.PixelWidth, result.RenderedFrame.PixelHeight);

            _workingDocument = result.Document;
            _savedDocument = result.Document;
            _undoStack.Clear();
            _redoStack.Clear();
            ResetCropSelection();
            LoadToneControlsFromDocument(result.Document);
            IsCropMode = false;

            CurrentFileName = result.SourceFile.FileName;
            UpdateCurrentFileDetails(
                result.RenderedFrame.PixelWidth,
                result.RenderedFrame.PixelHeight,
                result.SourceFile.Extension);

            ShowOriginal = false;
            IsFitMode = true;
            ZoomPercent = 100;
            HasImage = true;
            UpdateEditState();

            StatusMessage = result.HasEnabledEdits
                ? "Gespeicherte Bearbeitungen wurden nicht-destruktiv auf die Vorschau angewendet."
                : "Vorschau geladen. Das Original wurde nicht verändert.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer selection superseded this request.
        }
        catch (Exception exception)
        {
            HasImage = false;
            HasError = true;
            ErrorMessage = exception.GetBaseException().Message;
            StatusMessage = "Das Bild konnte nicht im Editor geöffnet werden.";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    public void UpdateCropSelection(double left, double top, double width, double height)
    {
        if (!IsCropMode)
        {
            return;
        }

        const double minimumNormalizedSize = 0.000001d;
        left = Math.Clamp(left, 0d, 1d - minimumNormalizedSize);
        top = Math.Clamp(top, 0d, 1d - minimumNormalizedSize);

        var maximumWidth = 1d - left;
        var maximumHeight = 1d - top;
        width = Math.Clamp(width, Math.Min(0.001d, maximumWidth), maximumWidth);
        height = Math.Clamp(height, Math.Min(0.001d, maximumHeight), maximumHeight);

        CropLeft = left;
        CropTop = top;
        CropWidth = width;
        CropHeight = height;
    }

    private void StartCrop()
    {
        if (!CanStartCrop())
        {
            return;
        }

        CancelTonePreview();
        ShowOriginal = false;
        IsFitMode = true;
        ZoomPercent = 100;
        ResetCropSelection();
        IsCropMode = true;
        StatusMessage = "Zuschneiden: Rahmen oder Griffe ziehen, danach Anwenden oder Abbrechen.";
    }

    private async Task ApplyCropAsync()
    {
        var before = _workingDocument;
        if (before is null || !CanApplyCrop())
        {
            return;
        }

        const double epsilon = 0.000001d;
        if (CropLeft <= epsilon
            && CropTop <= epsilon
            && CropWidth >= 1d - epsilon
            && CropHeight >= 1d - epsilon)
        {
            IsCropMode = false;
            StatusMessage = "Zuschneiden beendet. Der vollständige Bildbereich wurde beibehalten.";
            return;
        }

        await ApplyOperationAsync(
            BasicImageEditOperations.Crop(CropLeft, CropTop, CropWidth, CropHeight),
            "Bild zugeschnitten");

        if (!ReferenceEquals(before, _workingDocument))
        {
            IsCropMode = false;
            ResetCropSelection();
        }
    }

    private void CancelCrop()
    {
        if (!CanCancelCrop())
        {
            return;
        }

        IsCropMode = false;
        ResetCropSelection();
        StatusMessage = "Zuschneiden abgebrochen. Es wurde kein Bearbeitungsschritt hinzugefügt.";
    }

    private void ResetCropSelection()
    {
        CropLeft = 0d;
        CropTop = 0d;
        CropWidth = 1d;
        CropHeight = 1d;
    }

    private async Task ApplyToneAsync()
    {
        var current = _workingDocument;
        if (current is null || !CanApplyTone())
        {
            return;
        }

        CancelTonePreview();
        var candidate = BuildToneCandidateDocument(current);
        if (!await TryRenderWorkingDocumentAsync(candidate, "Tonwerte werden übernommen …"))
        {
            return;
        }

        _undoStack.Push(current);
        _redoStack.Clear();
        _workingDocument = candidate;
        CommitToneControlState(candidate);
        UpdateEditState();
        RefreshEditorCommandStates();
        StatusMessage = "Tonwerte übernommen. Die Regler bleiben auf dem aktuellen Wert; Änderungen sind noch nicht gespeichert.";
    }

    private void ResetToneDraft()
    {
        SetToneControlValues(0d, 0d, 0d, suppressPreview: false);
    }

    private void OnToneControlChanged()
    {
        OnPropertyChanged(nameof(HasToneDraft));
        OnPropertyChanged(nameof(HasToneAdjustment));
        OpenSelectedCommand.NotifyCanExecuteChanged();
        RefreshEditorCommandStates();

        if (_suppressTonePreview || !HasImage || IsBusy || IsCropMode || _workingDocument is null)
        {
            return;
        }

        ScheduleTonePreview();
    }

    private void ScheduleTonePreview()
    {
        CancelTonePreview();

        var cancellation = new CancellationTokenSource();
        _tonePreviewCancellation = cancellation;
        var generation = ++_tonePreviewGeneration;
        _ = RenderTonePreviewAfterDelayAsync(generation, cancellation.Token);
    }

    private async Task RenderTonePreviewAfterDelayAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TonePreviewDebounceMilliseconds, cancellationToken);

            var baseDocument = _workingDocument;
            if (baseDocument is null)
            {
                return;
            }

            var candidate = BuildToneCandidateDocument(baseDocument);
            var result = await _previewService.RenderAsync(
                new ImageEditPreviewRequest(
                    candidate.MediaItemId,
                    DefaultPreviewMaxEdge,
                    candidate),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _tonePreviewGeneration || !ReferenceEquals(baseDocument, _workingDocument))
            {
                return;
            }

            var renderedBitmap = DecodedImageFrameBitmapFactory.Create(result.RenderedFrame);
            ReplaceRenderedBitmap(renderedBitmap);
            UpdateRenderedDimensions(result.RenderedFrame.PixelWidth, result.RenderedFrame.PixelHeight);
            UpdateCurrentFileDetails(
                result.RenderedFrame.PixelWidth,
                result.RenderedFrame.PixelHeight,
                result.SourceFile.Extension);
            ShowOriginal = false;
            StatusMessage = HasToneDraft
                ? "Live-Vorschau aktiv · Anwenden übernimmt die aktuellen Reglerwerte in den Bearbeitungsstand."
                : "Tonwerte entsprechen wieder dem übernommenen Stand.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer slider value superseded this preview.
        }
        catch (Exception exception)
        {
            if (generation != _tonePreviewGeneration)
            {
                return;
            }

            HasError = true;
            ErrorMessage = exception.GetBaseException().Message;
            StatusMessage = "Die Live-Vorschau der Tonwerte konnte nicht gerendert werden.";
        }
    }

    private ImageEditDocument BuildToneCandidateDocument(ImageEditDocument baseDocument)
    {
        var operations = baseDocument.Operations.ToList();
        var existingIndex = _editableToneOperationId is null
            ? -1
            : operations.FindIndex(operation =>
                string.Equals(operation.Id, _editableToneOperationId, StringComparison.Ordinal));

        if (IsToneNeutral(ExposureEv, Contrast, Saturation))
        {
            if (existingIndex >= 0)
            {
                operations.RemoveAt(existingIndex);
            }

            return baseDocument with { Operations = operations };
        }

        var toneOperation = BasicImageEditOperations.Tone(
            ExposureEv,
            Contrast,
            Saturation,
            existingIndex >= 0 ? operations[existingIndex].Id : _editableToneOperationId);

        if (existingIndex >= 0)
        {
            operations[existingIndex] = toneOperation;
        }
        else
        {
            operations.Add(toneOperation);
        }

        return baseDocument with { Operations = operations };
    }

    private void LoadToneControlsFromDocument(ImageEditDocument document)
    {
        var toneOperation = document.Operations
            .LastOrDefault(operation =>
                operation.IsEnabled
                && string.Equals(
                    operation.TypeId,
                    BasicImageEditOperationTypeIds.Tone,
                    StringComparison.OrdinalIgnoreCase)
                && operation.Version == ToneImageEditOperationProcessor.SupportedVersion);

        if (toneOperation is not null
            && ToneImageEditOperationProcessor.TryReadParameters(
                toneOperation,
                out var exposureEv,
                out var contrast,
                out var saturation))
        {
            _editableToneOperationId = toneOperation.Id;
            _committedExposureEv = exposureEv;
            _committedContrast = contrast;
            _committedSaturation = saturation;
            SetToneControlValues(exposureEv, contrast, saturation, suppressPreview: true);
            return;
        }

        _editableToneOperationId = null;
        _committedExposureEv = 0d;
        _committedContrast = 0d;
        _committedSaturation = 0d;
        SetToneControlValues(0d, 0d, 0d, suppressPreview: true);
    }

    private void CommitToneControlState(ImageEditDocument document)
    {
        var toneOperation = IsToneNeutral(ExposureEv, Contrast, Saturation)
            ? null
            : document.Operations
                .LastOrDefault(operation =>
                    operation.IsEnabled
                    && string.Equals(
                        operation.TypeId,
                        BasicImageEditOperationTypeIds.Tone,
                        StringComparison.OrdinalIgnoreCase)
                    && operation.Version == ToneImageEditOperationProcessor.SupportedVersion);

        _editableToneOperationId = toneOperation?.Id;
        _committedExposureEv = ExposureEv;
        _committedContrast = Contrast;
        _committedSaturation = Saturation;
        OnPropertyChanged(nameof(HasToneDraft));
        OnPropertyChanged(nameof(HasToneAdjustment));
        OpenSelectedCommand.NotifyCanExecuteChanged();
    }

    private void SetToneControlValues(
        double exposureEv,
        double contrast,
        double saturation,
        bool suppressPreview)
    {
        var oldSuppress = _suppressTonePreview;
        _suppressTonePreview = true;
        try
        {
            ExposureEv = exposureEv;
            Contrast = contrast;
            Saturation = saturation;
        }
        finally
        {
            _suppressTonePreview = oldSuppress;
        }

        OnPropertyChanged(nameof(HasToneDraft));
        OnPropertyChanged(nameof(HasToneAdjustment));
        RefreshEditorCommandStates();

        if (!suppressPreview && !_suppressTonePreview && HasImage && !IsBusy && !IsCropMode)
        {
            ScheduleTonePreview();
        }
    }

    private void CancelTonePreview()
    {
        _tonePreviewCancellation?.Cancel();
        _tonePreviewCancellation?.Dispose();
        _tonePreviewCancellation = null;
        _tonePreviewGeneration++;
    }

    private static bool AreToneValuesEqual(
        double leftExposure,
        double leftContrast,
        double leftSaturation,
        double rightExposure,
        double rightContrast,
        double rightSaturation)
    {
        const double epsilon = 0.0001d;
        return Math.Abs(leftExposure - rightExposure) <= epsilon
            && Math.Abs(leftContrast - rightContrast) <= epsilon
            && Math.Abs(leftSaturation - rightSaturation) <= epsilon;
    }

    private static bool IsToneNeutral(double exposureEv, double contrast, double saturation)
    {
        return AreToneValuesEqual(exposureEv, contrast, saturation, 0d, 0d, 0d);
    }

    private async Task ApplyOperationAsync(
        ImageEditOperation operation,
        string completedMessage)
    {
        var current = _workingDocument;
        if (current is null || !CanModifyDocument())
        {
            return;
        }

        CancelTonePreview();
        var candidate = current with
        {
            Operations = current.Operations.Append(operation).ToArray()
        };

        if (!await TryRenderWorkingDocumentAsync(candidate, "Bearbeitung wird gerendert …"))
        {
            return;
        }

        _undoStack.Push(current);
        _redoStack.Clear();
        _workingDocument = candidate;
        UpdateEditState();
        RefreshEditorCommandStates();
        StatusMessage = $"{completedMessage}. Änderungen sind noch nicht gespeichert.";
    }

    private async Task UndoAsync()
    {
        var current = _workingDocument;
        if (current is null || !CanUndo())
        {
            return;
        }

        CancelTonePreview();
        var target = RebaseToCurrentRevision(_undoStack.Peek(), current);
        if (!await TryRenderWorkingDocumentAsync(target, "Letzten Bearbeitungsschritt rückgängig machen …"))
        {
            return;
        }

        _undoStack.Pop();
        _redoStack.Push(current);
        _workingDocument = target;
        LoadToneControlsFromDocument(target);
        UpdateEditState();
        RefreshEditorCommandStates();
        StatusMessage = HasUnsavedChanges
            ? "Bearbeitung rückgängig gemacht. Änderungen sind noch nicht gespeichert."
            : "Bearbeitung rückgängig gemacht. Der gespeicherte Stand ist wiederhergestellt.";
    }

    private async Task RedoAsync()
    {
        var current = _workingDocument;
        if (current is null || !CanRedo())
        {
            return;
        }

        CancelTonePreview();
        var target = RebaseToCurrentRevision(_redoStack.Peek(), current);
        if (!await TryRenderWorkingDocumentAsync(target, "Bearbeitungsschritt wiederholen …"))
        {
            return;
        }

        _redoStack.Pop();
        _undoStack.Push(current);
        _workingDocument = target;
        LoadToneControlsFromDocument(target);
        UpdateEditState();
        RefreshEditorCommandStates();
        StatusMessage = HasUnsavedChanges
            ? "Bearbeitung wiederholt. Änderungen sind noch nicht gespeichert."
            : "Bearbeitung wiederholt. Der gespeicherte Stand ist wiederhergestellt.";
    }

    private async Task SaveEditsAsync()
    {
        var document = _workingDocument;
        if (document is null || !CanSaveEdits())
        {
            return;
        }

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = "Bearbeitungsstand wird gespeichert …";

        try
        {
            var saved = await _documentStore.SaveAsync(document);
            _workingDocument = saved;
            _savedDocument = saved;
            UpdateEditState();
            StatusMessage = saved.Operations.Any(operation => operation.IsEnabled)
                ? "Bearbeitungen gespeichert. Das Originalbild blieb unverändert."
                : "Leerer Bearbeitungsstand gespeichert. Das Originalbild blieb unverändert.";
        }
        catch (ImageEditConcurrencyException exception)
        {
            HasError = true;
            ErrorMessage = exception.Message;
            StatusMessage =
                "Der Bearbeitungsstand wurde zwischenzeitlich geändert. Öffne das Bild neu, bevor du weiter speicherst.";
        }
        catch (Exception exception)
        {
            HasError = true;
            ErrorMessage = exception.GetBaseException().Message;
            StatusMessage = "Der Bearbeitungsstand konnte nicht gespeichert werden.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> TryRenderWorkingDocumentAsync(
        ImageEditDocument document,
        string progressMessage)
    {
        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = progressMessage;

        try
        {
            var result = await _previewService.RenderAsync(
                new ImageEditPreviewRequest(
                    document.MediaItemId,
                    DefaultPreviewMaxEdge,
                    document));

            var renderedBitmap = DecodedImageFrameBitmapFactory.Create(result.RenderedFrame);
            ReplaceRenderedBitmap(renderedBitmap);
            UpdateRenderedDimensions(result.RenderedFrame.PixelWidth, result.RenderedFrame.PixelHeight);
            UpdateCurrentFileDetails(
                result.RenderedFrame.PixelWidth,
                result.RenderedFrame.PixelHeight,
                result.SourceFile.Extension);

            ShowOriginal = false;
            return true;
        }
        catch (Exception exception)
        {
            HasError = true;
            ErrorMessage = exception.GetBaseException().Message;
            StatusMessage = "Die Vorschau der Bearbeitung konnte nicht gerendert werden.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static ImageEditDocument RebaseToCurrentRevision(
        ImageEditDocument target,
        ImageEditDocument current)
    {
        return target with
        {
            Revision = current.Revision,
            UpdatedUtc = current.UpdatedUtc
        };
    }

    private void UpdateEditState()
    {
        HasEnabledEdits = _workingDocument?.Operations.Any(operation => operation.IsEnabled) == true;
        HasUnsavedChanges = !HaveSameOperations(_workingDocument, _savedDocument);
        OnPropertyChanged(nameof(EditStateLabel));
        RefreshEditorCommandStates();
    }

    private static bool HaveSameOperations(
        ImageEditDocument? left,
        ImageEditDocument? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Operations.SequenceEqual(right.Operations);
    }

    private void RefreshEditorCommandStates()
    {
        RotateCounterClockwiseCommand.NotifyCanExecuteChanged();
        RotateClockwiseCommand.NotifyCanExecuteChanged();
        FlipHorizontalCommand.NotifyCanExecuteChanged();
        FlipVerticalCommand.NotifyCanExecuteChanged();
        StartCropCommand.NotifyCanExecuteChanged();
        ApplyCropCommand.NotifyCanExecuteChanged();
        CancelCropCommand.NotifyCanExecuteChanged();
        ApplyToneCommand.NotifyCanExecuteChanged();
        ResetToneCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SaveEditsCommand.NotifyCanExecuteChanged();
    }

    private void UpdateRenderedDimensions(int pixelWidth, int pixelHeight)
    {
        _renderedPixelWidth = pixelWidth;
        _renderedPixelHeight = pixelHeight;
        OnPropertyChanged(nameof(RenderedPixelWidth));
        OnPropertyChanged(nameof(RenderedPixelHeight));
        OnPropertyChanged(nameof(CropSelectionLabel));
        OnPropertyChanged(nameof(ZoomedWidth));
        OnPropertyChanged(nameof(ZoomedHeight));
    }

    private void UpdateCurrentFileDetails(
        int pixelWidth,
        int pixelHeight,
        string extension)
    {
        CurrentFileDetails =
            $"{pixelWidth} × {pixelHeight} px · {extension.TrimStart('.').ToUpperInvariant()}";
    }

    private void ReplaceBitmaps(Bitmap sourceBitmap, Bitmap renderedBitmap)
    {
        var oldSource = _sourceBitmap;
        var oldRendered = _renderedBitmap;

        _sourceBitmap = sourceBitmap;
        _renderedBitmap = renderedBitmap;
        UpdateDisplayBitmap();

        oldSource?.Dispose();
        if (!ReferenceEquals(oldRendered, oldSource))
        {
            oldRendered?.Dispose();
        }
    }

    private void ReplaceRenderedBitmap(Bitmap renderedBitmap)
    {
        var oldRendered = _renderedBitmap;
        _renderedBitmap = renderedBitmap;
        UpdateDisplayBitmap();

        if (!ReferenceEquals(oldRendered, _sourceBitmap))
        {
            oldRendered?.Dispose();
        }
    }

    private void UpdateDisplayBitmap()
    {
        DisplayBitmap = ShowOriginal
            ? _sourceBitmap
            : _renderedBitmap;
    }

    public void Dispose()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        CancelTonePreview();

        _sourceBitmap?.Dispose();
        if (!ReferenceEquals(_renderedBitmap, _sourceBitmap))
        {
            _renderedBitmap?.Dispose();
        }

        _sourceBitmap = null;
        _renderedBitmap = null;
        _sourcePixelWidth = 0;
        _sourcePixelHeight = 0;
        _renderedPixelWidth = 0;
        _renderedPixelHeight = 0;
        _displayBitmap = null;
        IsCropMode = false;
        ResetCropSelection();
        _suppressTonePreview = true;
        ExposureEv = 0d;
        Contrast = 0d;
        Saturation = 0d;
        _suppressTonePreview = false;
        _committedExposureEv = 0d;
        _committedContrast = 0d;
        _committedSaturation = 0d;
        _editableToneOperationId = null;
        _workingDocument = null;
        _savedDocument = null;
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
