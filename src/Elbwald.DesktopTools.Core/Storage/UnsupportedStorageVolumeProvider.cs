using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.Storage;

public sealed class UnsupportedStorageVolumeProvider
    : IStorageVolumeProvider
{
    public StorageVolumeInfo? FindVolumeForPath(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        return null;
    }

    public IReadOnlyList<StorageVolumeInfo> GetAvailableVolumes()
    {
        return Array.Empty<StorageVolumeInfo>();
    }
}
