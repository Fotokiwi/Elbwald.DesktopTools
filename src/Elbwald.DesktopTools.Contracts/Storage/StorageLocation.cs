namespace Elbwald.DesktopTools.Contracts.Storage;

public sealed record StorageLocation(
    string VolumeId,
    string RelativePath,
    string? LastKnownMountPath = null,
    string? LastKnownAbsolutePath = null);
