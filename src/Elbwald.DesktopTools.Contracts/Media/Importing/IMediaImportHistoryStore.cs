namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public interface IMediaImportHistoryStore
{
    Task<MediaImportHistoryEntry?> LoadLastAsync(
        CancellationToken cancellationToken = default);

    Task SaveLastAsync(
        MediaImportHistoryEntry entry,
        CancellationToken cancellationToken = default);
}
