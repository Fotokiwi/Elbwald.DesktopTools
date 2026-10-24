namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public sealed record LibraryHealthIndexEndpointSummary(
    string EndpointId,
    int IndexedLocationCount,
    int ReachableLocationCount,
    int OfflineLocationCount,
    int MissingLocationCount,
    int HistoricalLocationCount,
    int UnconfiguredLocationCount);
