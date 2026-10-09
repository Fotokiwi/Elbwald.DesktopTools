using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Projects;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class ProjectMediaItemViewModel
{
    public ProjectMediaItemViewModel(
        ProjectMediaReference reference,
        MediaIndexSearchResult? result)
    {
        ArgumentNullException.ThrowIfNull(reference);

        MediaItemId = reference.MediaItemId;
        AddedAtText = reference.AddedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

        if (result is null)
        {
            FileName = reference.MediaItemId;
            RelativePath = "Medium ist derzeit nicht im lokalen Medienindex auflösbar.";
            EndpointId = "–";
            MediaType = "Nicht aufgelöst";
            SizeText = "–";
            IsResolved = false;
            return;
        }

        FileName = result.RepresentativeRelativePath is null
            ? result.Item.Id
            : Path.GetFileName(result.RepresentativeRelativePath);
        RelativePath = result.RepresentativeRelativePath ?? "Kein aktueller Fundort";
        EndpointId = result.RepresentativeEndpointId ?? "–";
        MediaType = MediaIndexBrowseItemViewModel.GetMediaTypeLabel(result.Item.MediaType);
        SizeText = MediaIndexBrowseItemViewModel.FormatBytes(result.Item.Length);
        IsResolved = true;
    }

    public string MediaItemId { get; }
    public string FileName { get; }
    public string RelativePath { get; }
    public string EndpointId { get; }
    public string MediaType { get; }
    public string SizeText { get; }
    public string AddedAtText { get; }
    public bool IsResolved { get; }
}
