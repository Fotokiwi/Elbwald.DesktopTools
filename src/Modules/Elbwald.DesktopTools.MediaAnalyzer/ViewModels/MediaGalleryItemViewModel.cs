using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Elbwald.DesktopTools.MediaAnalyzer.ViewModels;

public sealed class MediaGalleryItemViewModel
    : ObservableObject,
      IDisposable
{
    private Bitmap? _thumbnail;
    private bool _hasThumbnail;

    public MediaGalleryItemViewModel(
        MediaFileItemViewModel fileItem,
        Action<MediaFileItemViewModel> selectAction)
    {
        ArgumentNullException.ThrowIfNull(fileItem);
        ArgumentNullException.ThrowIfNull(selectAction);

        FileItem = fileItem;

        FileName =
            fileItem.FileName;

        FormatLabel =
            fileItem.Extension.TrimStart('.').ToUpperInvariant();

        SelectCommand =
            new RelayCommand(
                () => selectAction(fileItem));
    }

    public MediaFileItemViewModel FileItem { get; }

    public string FileName { get; }

    public string FormatLabel { get; }

    public IRelayCommand SelectCommand { get; }

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (ReferenceEquals(
                    _thumbnail,
                    value))
            {
                return;
            }

            var previous =
                _thumbnail;

            if (SetProperty(
                    ref _thumbnail,
                    value))
            {
                previous?.Dispose();

                HasThumbnail =
                    value is not null;
            }
        }
    }

    public bool HasThumbnail
    {
        get => _hasThumbnail;
        private set => SetProperty(
            ref _hasThumbnail,
            value);
    }

    public void SetThumbnail(
        Bitmap? thumbnail)
    {
        Thumbnail =
            thumbnail;
    }

    public void Dispose()
    {
        Thumbnail = null;
    }
}
