namespace Elbwald.DesktopTools.Contracts.Maintenance;

public sealed record ApplicationDataResetResult(
    int DeletedFileCount,
    long DeletedBytes,
    IReadOnlyList<string> ClearedTargets);
