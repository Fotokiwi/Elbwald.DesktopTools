namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexFolderNode(
    string EndpointId,
    string RelativeFolder,
    string Name,
    int MediaCount,
    bool HasChildren);
