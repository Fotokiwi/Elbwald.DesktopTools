using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.Core.Recovery;

public sealed class StartupRecoveryService
    : IStartupRecoveryService
{
    private readonly IOperationJournal _journal;
    private readonly IFileOperationRecoveryInspector _inspector;
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    private StartupRecoverySnapshot _current =
        StartupRecoverySnapshot.NotScanned;

    public StartupRecoveryService(
        IOperationJournal journal,
        IFileOperationRecoveryInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(inspector);

        _journal = journal;
        _inspector = inspector;
    }

    public StartupRecoverySnapshot Current =>
        _current;

    public async Task<StartupRecoverySnapshot> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(cancellationToken);

        try
        {
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
