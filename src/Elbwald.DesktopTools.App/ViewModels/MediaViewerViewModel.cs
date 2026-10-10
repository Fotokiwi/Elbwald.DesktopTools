using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.Media;
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

    public MediaViewerViewModel(
        IMediaThumbnailService thumbnailService,
        MediaThumbnailOptions thumbnailOptions,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver,
        IReadOnlyList<MediaBrowserItemViewModel> items,
        string startMediaItemId)
    {
        _thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        _thumbnailOptions = thumbnailOptions ?? throw new ArgumentNullException(nameof(thumbnailOptions));
        _storageSettingsStore = storageSettingsStore ?? throw new ArgumentNullException(nameof(storageSettingsStore));
        _storageLocationResolver = storageLocationResolver ?? throw new ArgumentNullException(nameof(storageLocationResolver));
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

        _ = InitializeAsync();
    }

    public IReadOnlyList<int> PresentationIntervals { get; } = [3, 5, 10, 15, 30];
    public int ItemCount => _items.Count;
    public bool CanNavigate => _items.Count > 1;
    public bool HasImage => Image is not null;
    public bool HasNoImage => Image is null;
    public bool ShowControls => !IsPresentationRunning;

    partial void OnImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(HasNoImage));
    }

    partial void OnIsPresentationRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowControls));
    }

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
        PositionText = $"{_currentIndex + 1:N0} / {_items.Count:N0}";
        StatusText = "Bild wird geladen …";

        try
        {
            if (_endpointRoots.Count == 0)
            {
                await LoadEndpointRootsAsync(token);
            }

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
