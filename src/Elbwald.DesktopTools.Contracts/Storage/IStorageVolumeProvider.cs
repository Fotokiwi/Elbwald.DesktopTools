namespace Elbwald.DesktopTools.Contracts.Storage;

public interface IStorageVolumeProvider
{
    StorageVolumeInfo? FindVolumeForPath(string absolutePath);

    IReadOnlyList<StorageVolumeInfo> GetAvailableVolumes();
}
