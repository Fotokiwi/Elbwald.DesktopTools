using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Core.FileOperations;

public sealed class UnsupportedFileTrashService : IFileTrashService
{
    public Task<FileTrashResult> MoveToTrashAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path);

        return Task.FromResult(new FileTrashResult(
            false,
            fullPath ?? string.Empty,
            null,
            "Der Systempapierkorb wird auf diesem Betriebssystem noch nicht unterstützt. Die Datei wurde nicht verändert."));
    }
}
