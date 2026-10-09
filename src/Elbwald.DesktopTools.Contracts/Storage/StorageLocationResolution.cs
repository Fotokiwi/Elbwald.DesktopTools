namespace Elbwald.DesktopTools.Contracts.Storage;

public sealed record StorageLocationResolution(
    StorageLocationStatus Status,
    string? ResolvedPath,
    StorageVolumeInfo? Volume,
    string Message)
{
    public bool IsAvailable =>
        Status == StorageLocationStatus.Available
        && !string.IsNullOrWhiteSpace(ResolvedPath);
}
