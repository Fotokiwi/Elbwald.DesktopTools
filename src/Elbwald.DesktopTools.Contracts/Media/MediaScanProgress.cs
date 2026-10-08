namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record MediaScanProgress(
    int DirectoriesVisited,
    int FilesDiscovered,
    int FilesIncluded,
    int Errors,
    int SkippedSymbolicLinkDirectories);
