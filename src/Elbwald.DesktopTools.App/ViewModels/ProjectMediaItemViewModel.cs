using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Projects;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class ProjectMediaItemViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private Bitmap? thumbnail;

    [ObservableProperty]
    private bool thumbnailLoading;

    [ObservableProperty]
    private string thumbnailStatus = "Vorschau wird geladen …";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighlightActionText))]
    [NotifyPropertyChangedFor(nameof(HighlightBadgeText))]
    private bool isHighlight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoverBadgeText))]
    private bool isCover;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjectDate))]
    [NotifyPropertyChangedFor(nameof(ProjectDateText))]
    [NotifyPropertyChangedFor(nameof(ProjectDayBadgeText))]
    private DateOnly? projectDate;

    public ProjectMediaItemViewModel(
        ProjectMediaReference reference,
        MediaIndexSearchResult? result,
        bool isCover = false)
    {
        ArgumentNullException.ThrowIfNull(reference);

        Reference = reference;
        Result = result;
        MediaItemId = reference.MediaItemId;
        AddedAtText = reference.AddedAtUtc.ToLocalTime().ToString("dd.MM.yyyy");
        IsHighlight = reference.IsHighlight;
        IsCover = isCover;
        ProjectDate = reference.ProjectDate;

        if (result is null)
        {
            FileName = reference.MediaItemId;
            DisplayTitle = reference.MediaItemId;
            RelativePath = "Medium ist derzeit nicht im lokalen Medienindex auflösbar.";
            EndpointId = "–";
            MediaTypeValue = MediaFileType.Unknown;
            MediaType = "Nicht aufgelöst";
            SizeText = "–";
            IsResolved = false;
            ThumbnailStatus = "Medium nicht verfügbar";
            return;
        }

        FileName = result.RepresentativeRelativePath is null
            ? result.Item.Id
            : Path.GetFileName(result.RepresentativeRelativePath);
        DisplayTitle = !string.IsNullOrWhiteSpace(result.Title)
            ? result.Title.Trim()
            : Path.GetFileNameWithoutExtension(FileName);
        RelativePath = result.RepresentativeRelativePath ?? "Kein aktueller Fundort";
        EndpointId = result.RepresentativeEndpointId ?? "–";
        MediaTypeValue = result.Item.MediaType;
        MediaType = MediaIndexBrowseItemViewModel.GetMediaTypeLabel(result.Item.MediaType);
        SizeText = MediaIndexBrowseItemViewModel.FormatBytes(result.Item.Length);
        IsResolved = true;
        CaptureDate = result.CaptureDate;
        ThumbnailStatus = IsImage ? "Vorschau wird geladen …" : MediaType;
    }

    public ProjectMediaReference Reference { get; }
    public MediaIndexSearchResult? Result { get; }
    public string MediaItemId { get; }
    public string FileName { get; }
    public string DisplayTitle { get; }
    public string RelativePath { get; }
    public string EndpointId { get; }
    public MediaFileType MediaTypeValue { get; }
    public string MediaType { get; }
    public string SizeText { get; }
    public string AddedAtText { get; }
    public bool IsResolved { get; }
    public bool IsImage => MediaTypeValue == MediaFileType.Image;
    public bool HasThumbnail => Thumbnail is not null;
    public bool HasNoThumbnail => Thumbnail is null;
    public string HighlightActionText => IsHighlight ? "Highlight entfernen" : "Als Highlight markieren";
    public string HighlightBadgeText => IsHighlight ? "★ Highlight" : string.Empty;
    public string CoverBadgeText => IsCover ? "Cover" : string.Empty;
    public DateTime? CaptureDate { get; }
    public bool HasProjectDate => ProjectDate.HasValue;
    public string ProjectDateText => ProjectDate?.ToString("dd.MM.yyyy") ?? string.Empty;
    public string ProjectDayBadgeText => ProjectDate.HasValue
        ? $"Tag · {ProjectDate.Value:dd.MM.yyyy}"
        : string.Empty;

    partial void OnThumbnailChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(HasNoThumbnail));
    }

    public void SetThumbnail(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

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

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
