namespace Elbwald.DesktopTools.Contracts.Storage;

public interface IStorageSettingsStore
{
    Task<StorageSettings> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        StorageSettings settings,
        CancellationToken cancellationToken = default);
}
