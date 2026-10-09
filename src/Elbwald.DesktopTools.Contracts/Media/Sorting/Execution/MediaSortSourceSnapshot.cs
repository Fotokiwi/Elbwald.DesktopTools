namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public sealed record MediaSortSourceSnapshot(
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc);
