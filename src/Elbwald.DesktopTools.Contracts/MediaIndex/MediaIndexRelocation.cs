namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexRelocation(
    string MediaItemId,
    string PreviousEndpointId,
    string PreviousRelativePath,
    string CurrentEndpointId,
    string CurrentRelativePath);
