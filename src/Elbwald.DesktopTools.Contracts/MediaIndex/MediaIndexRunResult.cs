namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexRunResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int EndpointCount,
    int FilesDiscovered,
    int FilesIndexed,
    int NewItems,
    int NewLocations,
    int ReusedHashes,
    int LocationsMarkedMissing,
    IReadOnlyList<MediaIndexRelocation> Relocations,
    IReadOnlyList<MediaIndexIssue> Issues)
{
    public bool CompletedWithIssues => Issues.Count > 0;

    public int RelocationCount => Relocations.Count;

    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
}
