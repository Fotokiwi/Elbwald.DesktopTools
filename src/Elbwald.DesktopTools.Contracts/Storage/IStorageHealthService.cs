namespace Elbwald.DesktopTools.Contracts.Storage;

public interface IStorageHealthService
{
    Task<StorageHealthSnapshot> InspectFailureAsync(
        string filePath,
        Exception exception,
        CancellationToken cancellationToken = default);
}
