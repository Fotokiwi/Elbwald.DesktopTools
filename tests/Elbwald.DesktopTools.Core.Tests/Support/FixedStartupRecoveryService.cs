using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.Core.Tests.Support;

internal sealed class FixedStartupRecoveryService
    : IStartupRecoveryService
{
    private readonly StartupRecoverySnapshot _snapshot;

    public FixedStartupRecoveryService(
        StartupRecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
    }

    public StartupRecoverySnapshot Current =>
        _snapshot;

    public Task<StartupRecoverySnapshot> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_snapshot);
    }

    public static FixedStartupRecoveryService Clean()
    {
        return new FixedStartupRecoveryService(
            new StartupRecoverySnapshot(
                StartupRecoveryState.Clean,
                DateTimeOffset.UtcNow,
                Array.Empty<FileOperationRecoveryCandidate>(),
                corruptJournalLineCount: 0));
    }
}
