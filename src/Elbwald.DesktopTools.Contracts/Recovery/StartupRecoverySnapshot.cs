namespace Elbwald.DesktopTools.Contracts.Recovery;

public sealed class StartupRecoverySnapshot
{
    public StartupRecoverySnapshot(
        StartupRecoveryState state,
        DateTimeOffset? scannedAtUtc,
        IEnumerable<FileOperationRecoveryCandidate> candidates,
        int corruptJournalLineCount,
        string? errorMessage = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (corruptJournalLineCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(corruptJournalLineCount));
        }

        State = state;
        ScannedAtUtc = scannedAtUtc;
        Candidates = candidates.ToArray();
        CorruptJournalLineCount = corruptJournalLineCount;
        ErrorMessage = errorMessage;
    }

    public static StartupRecoverySnapshot NotScanned { get; } =
        new(
            StartupRecoveryState.NotScanned,
            scannedAtUtc: null,
            Array.Empty<FileOperationRecoveryCandidate>(),
            corruptJournalLineCount: 0);

    public StartupRecoveryState State { get; }

    public DateTimeOffset? ScannedAtUtc { get; }

    public IReadOnlyList<FileOperationRecoveryCandidate> Candidates { get; }

    public int CorruptJournalLineCount { get; }

    public string? ErrorMessage { get; }

    public bool HasRecoveryCandidates =>
        Candidates.Count > 0;

    public bool HasJournalCorruption =>
        CorruptJournalLineCount > 0;

    public bool RequiresAttention =>
        State is
            StartupRecoveryState.AttentionRequired
            or StartupRecoveryState.ScanFailed;

    public bool CanStartFileOperations =>
        State == StartupRecoveryState.Clean;
}
