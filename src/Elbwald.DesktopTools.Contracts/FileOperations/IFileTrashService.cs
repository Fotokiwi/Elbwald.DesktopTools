namespace Elbwald.DesktopTools.Contracts.FileOperations;

public interface IFileTrashService
{
    Task<FileTrashResult> MoveToTrashAsync(
        string path,
        CancellationToken cancellationToken = default);
}
