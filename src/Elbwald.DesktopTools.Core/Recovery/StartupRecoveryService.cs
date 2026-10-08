using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.Core.Recovery;

public sealed class StartupRecoveryService
    : IStartupRecoveryService
{
    private readonly IOperationJournal _journal;
    private readonly IFileOperationRecoveryInspector _inspector;
    private readonly IFileOperationProcessLock _processLock;
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    private StartupRecoverySnapshot _current =
        StartupRecoverySnapshot.NotScanned;

    public StartupRecoveryService(
        IOperationJournal journal,
        IFileOperationRecoveryInspector inspector,
        IFileOperationProcessLock processLock)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(processLock);

        _journal = journal;
        _inspector = inspector;
        _processLock = processLock;
    }

    public StartupRecoverySnapshot Current =>
        _current;

    public async Task<StartupRecoverySnapshot> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(
            cancellationToken);

        try
        {
            var lockResult =
                await _processLock.TryAcquireAsync(
                    cancellationToken);

            if (!lockResult.IsAcquired)
            {
                var state =
                    lockResult.State
                        == FileOperationProcessLockAcquireState.Unavailable
                    ? StartupRecoveryState.ProcessLockUnavailable
                    : StartupRecoveryState.ScanFailed;

                _current = new StartupRecoverySnapshot(
                    state,
                    DateTimeOffset.UtcNow,
                    Array.Empty<FileOperationRecoveryCandidate>(),
                    corruptJournalLineCount: 0,
                    errorMessage: lockResult.Message);

                return _current;
            }

            try
            {
                var journalResult =
                    await _journal.ReadAsync(
                        cancellationToken);

                var candidates =
                    await _inspector.InspectAsync(
                        cancellationToken);

                var state =
                    candidates.Count > 0
                    || journalResult.HasCorruption
                        ? StartupRecoveryState.AttentionRequired
                        : StartupRecoveryState.Clean;

                _current = new StartupRecoverySnapshot(
                    state,
                    DateTimeOffset.UtcNow,
                    candidates,
                    journalResult.SkippedCorruptLineCount);

                return _current;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or InvalidOperationException
                    or NotSupportedException)
            {
                _current = new StartupRecoverySnapshot(
                    StartupRecoveryState.ScanFailed,
                    DateTimeOffset.UtcNow,
                    Array.Empty<FileOperationRecoveryCandidate>(),
                    corruptJournalLineCount: 0,
                    errorMessage: exception.Message);

                return _current;
            }
        }
        finally
        {
            _scanGate.Release();
        }
    }
}
