namespace Elbwald.DesktopTools.Contracts.Recovery;

public interface IPersistentRecoveryStore : IRecoveryStore
{
    Task<bool> VerifyAsync(
        RecoveryHandle handle,
        CancellationToken cancellationToken = default);
}
