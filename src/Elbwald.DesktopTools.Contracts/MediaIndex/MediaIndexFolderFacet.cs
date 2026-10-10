namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexFolderFacet(
    string EndpointId,
    string RelativeFolder,
    int MediaCount);
