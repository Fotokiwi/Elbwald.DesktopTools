using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class ProjectSearchResultViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    public ProjectSearchResultViewModel(MediaIndexSearchResult result, bool isSelected = false)
    {
        ArgumentNullException.ThrowIfNull(result);
        IsSelected = isSelected;
        MediaItemId = result.Item.Id;
        RelativePath = result.RepresentativeRelativePath ?? "Kein aktueller Fundort";
        FileName = result.RepresentativeRelativePath is null ? result.Item.Id : Path.GetFileName(result.RepresentativeRelativePath);
        MediaType = MediaIndexBrowseItemViewModel.GetMediaTypeLabel(result.Item.MediaType);
        SizeText = MediaIndexBrowseItemViewModel.FormatBytes(result.Item.Length);
        LocationSummary = $"{result.PresentLocationCount:N0} aktuell · {result.HistoricalLocationCount:N0} historisch";
        CaptureDateText = result.CaptureDate?.ToString("dd.MM.yyyy HH:mm") ?? "Datum unbekannt";
        RatingText = result.Rating > 0 ? new string('★', Math.Clamp(result.Rating, 0, 3)) : string.Empty;
    }

    public string MediaItemId { get; }
    public string FileName { get; }
    public string RelativePath { get; }
    public string MediaType { get; }
    public string SizeText { get; }
    public string LocationSummary { get; }
    public string CaptureDateText { get; }
    public string RatingText { get; }
}
