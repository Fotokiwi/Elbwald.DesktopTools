using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.Image.Editor.Basic.ViewModels;

public sealed class ImageEditorMediaItemViewModel
{
    public ImageEditorMediaItemViewModel(MediaIndexSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        MediaItemId = result.Item.Id;
        DisplayName = !string.IsNullOrWhiteSpace(result.Title)
            ? result.Title
            : !string.IsNullOrWhiteSpace(result.RepresentativeRelativePath)
                ? Path.GetFileName(result.RepresentativeRelativePath)
                : result.Item.Id;

        RelativePath = result.RepresentativeRelativePath ?? string.Empty;
        CaptureDate = result.CaptureDate;
    }

    public string MediaItemId { get; }

    public string DisplayName { get; }

    public string RelativePath { get; }

    public DateTime? CaptureDate { get; }

    public override string ToString() => DisplayName;
}
