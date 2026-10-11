using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Knowledge;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.App.Services;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MediaViewerViewModel : ObservableObject, IDisposable
{
    private readonly IMediaThumbnailService _thumbnailService;
    private readonly MediaThumbnailOptions _thumbnailOptions;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageLocationResolver;
    private readonly IMediaIndexService _mediaIndexService;
    private readonly IReadOnlyList<MediaBrowserItemViewModel> _items;
    private readonly DispatcherTimer _presentationTimer;
    private CancellationTokenSource? _loadCancellation;
    private IReadOnlyDictionary<string, string> _endpointRoots = new Dictionary<string, string>();
    private int _currentIndex;
    private bool _advancing;

    [ObservableProperty] private Bitmap? image;
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private string positionText = string.Empty;
    [ObservableProperty] private string statusText = "Bild wird geladen …";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isPresentationRunning;
    [ObservableProperty] private bool randomOrder;
    [ObservableProperty] private int selectedIntervalSeconds = 5;
    [ObservableProperty] private int rating;
    [ObservableProperty] private string ratingText = "Unbewertet";
    [ObservableProperty] private string knowledgeSummary = "–";
    [ObservableProperty] private bool showQuickEdit;
    [ObservableProperty] private string quickKnowledgeKind = "Tag";
    [ObservableProperty] private string quickKnowledgeText = string.Empty;
    [ObservableProperty] private IReadOnlyList<QuickKnowledgeItemViewModel> quickKnowledgeItems = Array.Empty<QuickKnowledgeItemViewModel>();
    [ObservableProperty] private string quickEditStatus = "Bewertung und Zuordnungen werden direkt in Elbwald gespeichert.";

    public MediaViewerViewModel(
        IMediaThumbnailService thumbnailService,
        MediaThumbnailOptions thumbnailOptions,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver,
        IMediaIndexService mediaIndexService,
        IReadOnlyList<MediaBrowserItemViewModel> items,
        string startMediaItemId)
    {
        _thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        _thumbnailOptions = thumbnailOptions ?? throw new ArgumentNullException(nameof(thumbnailOptions));
        _storageSettingsStore = storageSettingsStore ?? throw new ArgumentNullException(nameof(storageSettingsStore));
        _storageLocationResolver = storageLocationResolver ?? throw new ArgumentNullException(nameof(storageLocationResolver));
        _mediaIndexService = mediaIndexService ?? throw new ArgumentNullException(nameof(mediaIndexService));
        _items = items?.Where(item => item.IsImage).ToArray() ?? throw new ArgumentNullException(nameof(items));

        if (_items.Count == 0)
        {
            throw new ArgumentException("Der Viewer benötigt mindestens ein Bild.", nameof(items));
        }

        _currentIndex = Math.Max(0, _items.ToList().FindIndex(item =>
            string.Equals(item.Id, startMediaItemId, StringComparison.OrdinalIgnoreCase)));

        _presentationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(SelectedIntervalSeconds)
        };
        _presentationTimer.Tick += OnPresentationTick;

        SetRatingCommand = new AsyncRelayCommand<string>(SetRatingAsync, _ => !IsBusy);
        ToggleQuickEditCommand = new RelayCommand(() => ShowQuickEdit = !ShowQuickEdit);
        AddQuickKnowledgeCommand = new AsyncRelayCommand(AddQuickKnowledgeAsync, CanAddQuickKnowledge);

        _ = InitializeAsync();
    }

    public IReadOnlyList<int> PresentationIntervals { get; } = [3, 5, 10, 15, 30];
    public IReadOnlyList<string> QuickKnowledgeKinds { get; } = ["Tag", "Person", "Ort", "Ereignis"];
    public IAsyncRelayCommand<string> SetRatingCommand { get; }
    public IRelayCommand ToggleQuickEditCommand { get; }
    public IAsyncRelayCommand AddQuickKnowledgeCommand { get; }
    public int ItemCount => _items.Count;
    public bool CanNavigate => _items.Count > 1;
    public bool HasImage => Image is not null;
    public bool HasNoImage => Image is null;
    public bool ShowControls => !IsPresentationRunning;
    public bool ShowQuickEditPanel => ShowControls && ShowQuickEdit;

    partial void OnImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(HasNoImage));
    }

    partial void OnIsPresentationRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowControls));
        OnPropertyChanged(nameof(ShowQuickEditPanel));
        if (value) ShowQuickEdit = false;
    }

    partial void OnShowQuickEditChanged(bool value) =>
        OnPropertyChanged(nameof(ShowQuickEditPanel));

    partial void OnIsBusyChanged(bool value)
    {
        SetRatingCommand.NotifyCanExecuteChanged();
        AddQuickKnowledgeCommand.NotifyCanExecuteChanged();
    }

    partial void OnQuickKnowledgeKindChanged(string value)
    {
        QuickKnowledgeText = string.Empty;
        _ = LoadQuickKnowledgeItemsAsync(CancellationToken.None);
    }

    partial void OnQuickKnowledgeTextChanged(string value) =>
        AddQuickKnowledgeCommand.NotifyCanExecuteChanged();

    partial void OnSelectedIntervalSecondsChanged(int value)
    {
        _presentationTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(value, 1, 3600));
    }

    private async Task InitializeAsync()
    {
        await LoadEndpointRootsAsync(CancellationToken.None);
        await LoadCurrentAsync();
    }

    public Task PreviousAsync() => NavigateAsync(-1, wrap: true);
    public Task NextAsync() => NavigateAsync(1, wrap: true);

    public void TogglePresentation()
    {
        if (IsPresentationRunning)
        {
            StopPresentation();
        }
        else
        {
            StartPresentation();
        }
    }

    public void StartPresentation()
    {
        if (IsPresentationRunning)
        {
            return;
        }

        _presentationTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(SelectedIntervalSeconds, 1, 3600));
        IsPresentationRunning = true;
        StatusText = $"Präsentation · Wechsel alle {SelectedIntervalSeconds} Sekunden";
        _presentationTimer.Start();
    }

    public void StopPresentation()
    {
        _presentationTimer.Stop();
        IsPresentationRunning = false;
        StatusText = "Präsentation pausiert.";
    }

    private async void OnPresentationTick(object? sender, EventArgs e)
    {
        if (_advancing || !IsPresentationRunning)
        {
            return;
        }

        _advancing = true;
        try
        {
            if (RandomOrder && _items.Count > 1)
            {
                var next = _currentIndex;
                while (next == _currentIndex)
                {
                    next = Random.Shared.Next(_items.Count);
                }

                _currentIndex = next;
                await LoadCurrentAsync();
            }
            else
            {
                await NavigateAsync(1, wrap: true);
            }
        }
        finally
        {
            _advancing = false;
        }
    }

    private async Task NavigateAsync(int delta, bool wrap)
    {
        if (_items.Count <= 1 || IsBusy)
        {
            return;
        }

        var next = _currentIndex + delta;
        if (wrap)
        {
            next = (next % _items.Count + _items.Count) % _items.Count;
        }
        else if (next < 0 || next >= _items.Count)
        {
            return;
        }

        _currentIndex = next;
        await LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;

        IsBusy = true;
        var item = _items[_currentIndex];
        Title = item.DisplayTitle;
        Subtitle = item.RelativePath;
        Rating = item.Rating;
        RatingText = Rating == 0 ? "Unbewertet" : new string('★', Rating);
        KnowledgeSummary = "Metadaten werden geladen …";
        PositionText = $"{_currentIndex + 1:N0} / {_items.Count:N0}";
        StatusText = "Bild wird geladen …";

        try
        {
            if (_endpointRoots.Count == 0)
            {
                await LoadEndpointRootsAsync(token);
            }

            await LoadCurrentKnowledgeAsync(item, token);

            var file = TryResolveMediaFile(item.Result);
            if (file is null)
            {
                ReplaceImage(null);
                StatusText = "Originaldatei ist an keinem aktuellen Speicherort verfügbar.";
                return;
            }

            var preview = await _thumbnailService.GetIndexedThumbnailAsync(
                item.Id,
                file,
                _thumbnailOptions.ViewerWidth,
                token);

            token.ThrowIfCancellationRequested();
            if (!preview.IsSuccessful || preview.EncodedImage is not { Length: > 0 })
            {
                ReplaceImage(null);
                StatusText = "Für dieses Bild konnte keine Großansicht erzeugt werden.";
                return;
            }

            using var stream = new MemoryStream(preview.EncodedImage, writable: false);
            ReplaceImage(new Bitmap(stream));
            StatusText = IsPresentationRunning
                ? $"Präsentation · Wechsel alle {SelectedIntervalSeconds} Sekunden"
                : "Großansicht";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReplaceImage(null);
            StatusText = "Großansicht konnte nicht geladen werden: " + exception.GetBaseException().Message;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    public Task ApplyRatingShortcutAsync(int value) =>
        SetRatingAsync(Math.Clamp(value, 0, 3).ToString());

    private async Task SetRatingAsync(string? value)
    {
        if (!int.TryParse(value, out var ratingValue)) return;
        ratingValue = Math.Clamp(ratingValue, 0, 3);
        var item = _items[_currentIndex];
        try
        {
            await _mediaIndexService.SetRatingBatchAsync([item.Id], ratingValue);
            item.SetRating(ratingValue);
            Rating = ratingValue;
            RatingText = ratingValue == 0 ? "Unbewertet" : new string('★', ratingValue);
            QuickEditStatus = ratingValue == 0
                ? "Bewertung entfernt."
                : $"Mit {ratingValue} Stern(en) bewertet.";
        }
        catch (Exception exception)
        {
            QuickEditStatus = "Bewertung fehlgeschlagen: " + exception.GetBaseException().Message;
        }
    }

    private MediaKnowledgeKind GetQuickKnowledgeKind() => QuickKnowledgeKind switch
    {
        "Person" => MediaKnowledgeKind.Person,
        "Ort" => MediaKnowledgeKind.Place,
        "Ereignis" => MediaKnowledgeKind.Event,
        _ => MediaKnowledgeKind.Tag
    };

    private bool CanAddQuickKnowledge() =>
        !IsBusy && !string.IsNullOrWhiteSpace(QuickKnowledgeText);

    private async Task LoadQuickKnowledgeItemsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var items = await _mediaIndexService.GetKnowledgeItemsAsync(
                GetQuickKnowledgeKind(),
                limit: 8,
                cancellationToken: cancellationToken);
            QuickKnowledgeItems = items
                .Select(item => new QuickKnowledgeItemViewModel(item, ApplyQuickKnowledgeItemAsync))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            QuickKnowledgeItems = Array.Empty<QuickKnowledgeItemViewModel>();
        }
    }

    private async Task AddQuickKnowledgeAsync()
    {
        if (!CanAddQuickKnowledge()) return;
        var item = _items[_currentIndex];
        var name = QuickKnowledgeText.Trim();
        try
        {
            var knowledge = await _mediaIndexService.EnsureKnowledgeItemAsync(
                new MediaKnowledgeDraft(GetQuickKnowledgeKind(), name));
            var added = await _mediaIndexService.AssignKnowledgeItemBatchAsync([item.Id], knowledge.Id);
            QuickKnowledgeText = string.Empty;
            QuickEditStatus = added == 0
                ? $"„{knowledge.Name}“ war bereits zugeordnet."
                : $"„{knowledge.Name}“ wurde zugeordnet.";
            await LoadCurrentKnowledgeAsync(item, CancellationToken.None);
            await LoadQuickKnowledgeItemsAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            QuickEditStatus = "Zuordnung fehlgeschlagen: " + exception.GetBaseException().Message;
        }
    }

    private async Task ApplyQuickKnowledgeItemAsync(QuickKnowledgeItemViewModel knowledge)
    {
        var item = _items[_currentIndex];
        try
        {
            var added = await _mediaIndexService.AssignKnowledgeItemBatchAsync([item.Id], knowledge.Id);
            QuickEditStatus = added == 0
                ? $"„{knowledge.Name}“ war bereits zugeordnet."
                : $"„{knowledge.Name}“ wurde zugeordnet.";
            await LoadCurrentKnowledgeAsync(item, CancellationToken.None);
            await LoadQuickKnowledgeItemsAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            QuickEditStatus = "Zuordnung fehlgeschlagen: " + exception.GetBaseException().Message;
        }
    }

    private async Task LoadCurrentKnowledgeAsync(
        MediaBrowserItemViewModel item,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _mediaIndexService.GetKnowledgeAsync(item.Id, cancellationToken);
            Rating = snapshot.Rating;
            item.SetRating(snapshot.Rating);
            RatingText = snapshot.Rating == 0 ? "Unbewertet" : new string('★', snapshot.Rating);
            KnowledgeSummary = snapshot.Items.Count == 0
                ? "Noch keine Personen, Orte, Tags oder Ereignisse"
                : string.Join(" · ", snapshot.Items.Select(value => value.Name));
            await LoadQuickKnowledgeItemsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            KnowledgeSummary = "Metadaten konnten nicht geladen werden";
        }
    }

    private async Task LoadEndpointRootsAsync(CancellationToken cancellationToken)
    {
        var settings = await _storageSettingsStore.LoadAsync(cancellationToken);
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in settings.Endpoints.Where(endpoint => endpoint.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = _storageLocationResolver.Resolve(endpoint.Location);
            if (resolution.IsAvailable && !string.IsNullOrWhiteSpace(resolution.ResolvedPath))
            {
                roots[endpoint.Id] = Path.GetFullPath(resolution.ResolvedPath);
            }
        }

        _endpointRoots = roots;
    }

    private MediaFile? TryResolveMediaFile(MediaIndexSearchResult result)
    {
        if (!result.RepresentativeIsPresent
            || string.IsNullOrWhiteSpace(result.RepresentativeEndpointId)
            || string.IsNullOrWhiteSpace(result.RepresentativeRelativePath)
            || !_endpointRoots.TryGetValue(result.RepresentativeEndpointId, out var root))
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

    private void ReplaceImage(Bitmap? bitmap)
    {
        var old = Image;
        Image = bitmap;
        old?.Dispose();
    }

    public void Dispose()
    {
        _presentationTimer.Stop();
        _presentationTimer.Tick -= OnPresentationTick;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        Image?.Dispose();
        Image = null;
    }
}
