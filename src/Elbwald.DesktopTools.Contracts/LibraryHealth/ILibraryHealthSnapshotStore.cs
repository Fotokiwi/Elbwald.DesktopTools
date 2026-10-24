using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public interface ILibraryHealthSnapshotStore
{
    Task<LibraryHealthSnapshot?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        LibraryHealthReport report,
        StorageSettings settings,
        MediaIndexSummary mediaIndexSummary,
        CancellationToken cancellationToken = default);

    LibraryHealthSnapshotFreshness EvaluateFreshness(
        LibraryHealthSnapshot snapshot,
        StorageSettings settings,
        MediaIndexSummary mediaIndexSummary);
}
