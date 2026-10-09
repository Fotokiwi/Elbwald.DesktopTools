namespace Elbwald.DesktopTools.Contracts.Storage;

public interface IStorageLocationResolver
{
    StorageLocation Capture(string absolutePath);

    StorageLocationResolution Resolve(StorageLocation location);
}
