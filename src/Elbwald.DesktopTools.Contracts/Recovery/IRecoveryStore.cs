namespace Elbwald.DesktopTools.Contracts.Recovery;

public interface IRecoveryStore
{
    Task<RecoveryHandle> PreserveAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(
        RecoveryHandle handle,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        RecoveryHandle handle,
        CancellationToken cancellationToken = default);
}
