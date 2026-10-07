namespace Elbwald.DesktopTools.Contracts.Recovery;

public interface IStartupRecoveryService
{
    StartupRecoverySnapshot Current { get; }

    Task<StartupRecoverySnapshot> ScanAsync(
        CancellationToken cancellationToken = default);
}
