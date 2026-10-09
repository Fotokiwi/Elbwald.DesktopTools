namespace Elbwald.DesktopTools.Contracts.Storage;

public sealed record StorageVolumeInfo(
    string VolumeId,
    string MountPath,
    string? FileSystemType = null,
    string? DisplayName = null,
    long? CapacityBytes = null);
