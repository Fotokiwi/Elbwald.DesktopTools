using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public sealed record LibraryHealthEndpointResult(
    StorageEndpoint Endpoint,
    StorageLocationStatus StorageStatus,
    string? ResolvedPath,
    int FilesAnalyzed,
    int IssueCount,
    int MissingCaptureDateCount)
{
    public int ImageCount { get; init; }

    public int MissingDimensionsCount { get; init; }

    public int ConflictingCaptureDateCount { get; init; }

    public int SuspiciousCaptureDateCount { get; init; }

    public int MetadataErrorCount { get; init; }

    public int IndexedLocationCount { get; init; }

    public int ReachableIndexedLocationCount { get; init; }

    public int MissingIndexedLocationCount { get; init; }

    public int HistoricalIndexedLocationCount { get; init; }
}
