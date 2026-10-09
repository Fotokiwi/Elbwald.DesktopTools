using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class MediaIndexLocationItemViewModel
{
    public MediaIndexLocationItemViewModel(
        MediaIndexLocation location,
        IReadOnlyDictionary<string, string> endpointNames)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(endpointNames);

        EndpointName = endpointNames.TryGetValue(location.EndpointId, out var endpointName)
            ? endpointName
            : location.EndpointId;
        RelativePath = location.RelativePath;
        Status = location.IsPresent ? "Aktuell" : "Historisch";
        VolumeId = location.VolumeId;
        LastSeen = location.LastSeenUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
        SizeText = MediaIndexBrowseItemViewModel.FormatBytes(location.Length);
    }

    public string EndpointName { get; }
    public string RelativePath { get; }
    public string Status { get; }
    public string VolumeId { get; }
    public string LastSeen { get; }
    public string SizeText { get; }
}
