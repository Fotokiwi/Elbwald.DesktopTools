namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public sealed record LibraryHealthSnapshot(
    DateTimeOffset SavedAtUtc,
    LibraryHealthReport Report,
    string StorageSettingsFingerprint,
    LibraryHealthSnapshotIndexState IndexState);

public sealed record LibraryHealthSnapshotIndexState(
    int ItemCount,
    int PresentLocationCount,
    int HistoricalLocationCount,
    DateTimeOffset? LastIndexedUtc);

public sealed record LibraryHealthSnapshotFreshness(
    bool IsStale,
    bool StorageSettingsChanged,
    bool MediaIndexChanged)
{
    public static LibraryHealthSnapshotFreshness Current { get; } =
        new(false, false, false);
}
