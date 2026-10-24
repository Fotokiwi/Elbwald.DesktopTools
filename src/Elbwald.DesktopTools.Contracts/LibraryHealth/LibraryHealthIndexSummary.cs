namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public sealed record LibraryHealthIndexSummary(
    int IndexedMediaCount,
    int PresentMediaCount,
    int OrphanedMediaCount,
    int MissingCaptureDateCount,
    DateTimeOffset? LastIndexedUtc)
{
    public int OfflineMediaCount { get; init; }

    public int MissingMediaCount { get; init; }

    public int UnconfiguredMediaCount { get; init; }

    public int StaleLocationCount { get; init; }

    public IReadOnlyList<LibraryHealthIndexEndpointSummary> EndpointSummaries { get; init; } =
        Array.Empty<LibraryHealthIndexEndpointSummary>();

    public static LibraryHealthIndexSummary Empty { get; } =
        new(0, 0, 0, 0, null);
}
