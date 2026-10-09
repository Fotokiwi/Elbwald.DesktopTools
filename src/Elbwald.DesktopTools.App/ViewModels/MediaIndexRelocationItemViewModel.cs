using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class MediaIndexRelocationItemViewModel
{
    public MediaIndexRelocationItemViewModel(
        MediaIndexRelocation relocation,
        IReadOnlyDictionary<string, string> endpointNames)
    {
        ArgumentNullException.ThrowIfNull(relocation);
        ArgumentNullException.ThrowIfNull(endpointNames);

        MediaItemId = relocation.MediaItemId;
        PreviousEndpoint = GetEndpointName(relocation.PreviousEndpointId, endpointNames);
        PreviousPath = relocation.PreviousRelativePath;
        CurrentEndpoint = GetEndpointName(relocation.CurrentEndpointId, endpointNames);
        CurrentPath = relocation.CurrentRelativePath;
    }

    public string MediaItemId { get; }

    public string PreviousEndpoint { get; }

    public string PreviousPath { get; }

    public string CurrentEndpoint { get; }

    public string CurrentPath { get; }

    public string PreviousLocationText => $"{PreviousEndpoint} · {PreviousPath}";

    public string CurrentLocationText => $"{CurrentEndpoint} · {CurrentPath}";

    private static string GetEndpointName(
        string endpointId,
        IReadOnlyDictionary<string, string> endpointNames) =>
        endpointNames.TryGetValue(endpointId, out var name)
            ? name
            : endpointId;
}
