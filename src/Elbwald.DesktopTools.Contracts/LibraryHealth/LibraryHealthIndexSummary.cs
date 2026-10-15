namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public sealed record LibraryHealthIndexSummary(
    int IndexedMediaCount,
    int PresentMediaCount,
    int OrphanedMediaCount,
    int MissingCaptureDateCount,
    DateTimeOffset? LastIndexedUtc)
{
    public static LibraryHealthIndexSummary Empty { get; } =
        new(0, 0, 0, 0, null);
}
