using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class ProjectSearchResultViewModel
{
    public ProjectSearchResultViewModel(MediaIndexSearchResult result)
    {
        MediaItemId = result.Item.Id;
        RelativePath = result.RepresentativeRelativePath ?? "Kein aktueller Fundort";
        FileName = result.RepresentativeRelativePath is null ? result.Item.Id : Path.GetFileName(result.RepresentativeRelativePath);
        MediaType = MediaIndexBrowseItemViewModel.GetMediaTypeLabel(result.Item.MediaType);
        SizeText = MediaIndexBrowseItemViewModel.FormatBytes(result.Item.Length);
        LocationSummary = $"{result.PresentLocationCount:N0} aktuell · {result.HistoricalLocationCount:N0} historisch";
    }

    public string MediaItemId { get; }
    public string FileName { get; }
    public string RelativePath { get; }
    public string MediaType { get; }
    public string SizeText { get; }
    public string LocationSummary { get; }
}
