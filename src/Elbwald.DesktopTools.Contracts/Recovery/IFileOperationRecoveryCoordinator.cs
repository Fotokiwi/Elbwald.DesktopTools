namespace Elbwald.DesktopTools.Contracts.Recovery;

public interface IFileOperationRecoveryCoordinator
{
    Task<IReadOnlyList<FileOperationRecoveryResult>> RecoverPendingAsync(
        CancellationToken cancellationToken = default);
}
