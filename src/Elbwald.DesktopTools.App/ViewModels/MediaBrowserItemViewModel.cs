using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MediaBrowserItemViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private Bitmap? thumbnail;

    [ObservableProperty]
    private bool thumbnailLoading;

    [ObservableProperty]
    private string thumbnailStatus = "Vorschau wird geladen …";

    [ObservableProperty]
    private int rating;

    public MediaBrowserItemViewModel(
        MediaIndexSearchResult result,
        IReadOnlyDictionary<string, string> endpointNames)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(endpointNames);

        Result = result;
        Id = result.Item.Id;
        MediaTypeValue = result.Item.MediaType;
        MediaType = MediaIndexBrowseItemViewModel.GetMediaTypeLabel(result.Item.MediaType);
        SizeText = MediaIndexBrowseItemViewModel.FormatBytes(result.Item.Length);
        RelativePath = result.RepresentativeRelativePath ?? string.Empty;
        FileName = string.IsNullOrWhiteSpace(RelativePath)
            ? result.Item.Id
            : Path.GetFileName(RelativePath);
        DisplayTitle = !string.IsNullOrWhiteSpace(result.Title)
            ? result.Title.Trim()
            : Path.GetFileNameWithoutExtension(FileName);
        EndpointName = result.RepresentativeEndpointId is not null
            && endpointNames.TryGetValue(result.RepresentativeEndpointId, out var endpointName)
                ? endpointName
                : result.RepresentativeEndpointId ?? "Ohne Fundort";
        LocationSummary = $"{result.PresentLocationCount:N0} aktuell · {result.HistoricalLocationCount:N0} historisch";
        CaptureDateText = result.CaptureDate.HasValue
            ? result.CaptureDate.Value.ToString("dd.MM.yyyy HH:mm")
            : "Kein Aufnahmedatum";
        HasMetadataOverride = !string.IsNullOrWhiteSpace(result.Title)
                              || !string.IsNullOrWhiteSpace(result.Description);
        Rating = Math.Clamp(result.Rating, 0, 3);
    }

    public MediaIndexSearchResult Result { get; }
    public string Id { get; }
    public MediaFileType MediaTypeValue { get; }
    public string MediaType { get; }
    public string SizeText { get; }
    public string RelativePath { get; }
    public string FileName { get; }
    public string DisplayTitle { get; }
    public string EndpointName { get; }
    public string LocationSummary { get; }
    public string CaptureDateText { get; }
    public bool HasMetadataOverride { get; }
    public string RatingText => Rating == 0 ? string.Empty : new string('★', Rating);
    public bool HasRating => Rating > 0;
    public bool IsImage => MediaTypeValue == MediaFileType.Image;
    public bool HasThumbnail => Thumbnail is not null;
    public bool HasNoThumbnail => Thumbnail is null;

    partial void OnRatingChanged(int value)
    {
        OnPropertyChanged(nameof(RatingText));
        OnPropertyChanged(nameof(HasRating));
    }

    partial void OnThumbnailChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(HasNoThumbnail));
    }

    public void SetThumbnail(Bitmap bitmap)
    {
        Thumbnail?.Dispose();
        Thumbnail = bitmap;
        ThumbnailLoading = false;
        ThumbnailStatus = string.Empty;
    }

    public void SetThumbnailUnavailable(string message)
    {
        ThumbnailLoading = false;
        ThumbnailStatus = message;
    }

    public void SetRating(int value) => Rating = Math.Clamp(value, 0, 3);

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
