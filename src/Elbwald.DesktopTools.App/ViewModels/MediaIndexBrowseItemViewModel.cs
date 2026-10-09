using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class MediaIndexBrowseItemViewModel
{
    public MediaIndexBrowseItemViewModel(
        MediaIndexSearchResult result,
        IReadOnlyDictionary<string, string> endpointNames)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(endpointNames);

        Id = result.Item.Id;
        Sha256 = result.Item.Sha256;
        MediaType = GetMediaTypeLabel(result.Item.MediaType);
        SizeText = FormatBytes(result.Item.Length);
        EndpointName = result.RepresentativeEndpointId is not null
            && endpointNames.TryGetValue(result.RepresentativeEndpointId, out var endpointName)
                ? endpointName
                : result.RepresentativeEndpointId ?? "Ohne Fundort";
        RelativePath = result.RepresentativeRelativePath ?? "Kein Fundort gespeichert";
        PresenceText = result.RepresentativeRelativePath is null
            ? "Ohne Fundort"
            : result.RepresentativeIsPresent
                ? "Aktuell"
                : "Historisch";
        LocationSummary = $"{result.PresentLocationCount:N0} aktuell · {result.HistoricalLocationCount:N0} historisch";
    }

    public string Id { get; }
    public string Sha256 { get; }
    public string MediaType { get; }
    public string SizeText { get; }
    public string EndpointName { get; }
    public string RelativePath { get; }
    public string PresenceText { get; }
    public string LocationSummary { get; }

    internal static string GetMediaTypeLabel(MediaFileType type) => type switch
    {
        MediaFileType.Image => "Bild",
        MediaFileType.Video => "Video",
        MediaFileType.Audio => "Audio",
        _ => "Unbekannt"
    };

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes:N0} {units[unit]}"
            : $"{value:N1} {units[unit]}";
    }
}
