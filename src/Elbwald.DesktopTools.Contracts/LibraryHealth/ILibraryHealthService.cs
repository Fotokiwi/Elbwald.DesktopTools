using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public interface ILibraryHealthService
{
    Task<LibraryHealthReport> ScanAsync(
        StorageSettings settings,
        CancellationToken cancellationToken = default);
}
