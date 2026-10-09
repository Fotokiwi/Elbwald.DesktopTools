using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public sealed record LibraryHealthEndpointResult(
    StorageEndpoint Endpoint,
    StorageLocationStatus StorageStatus,
    string? ResolvedPath,
    int FilesAnalyzed,
    int IssueCount,
    int MissingCaptureDateCount);
