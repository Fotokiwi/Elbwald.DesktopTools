namespace Elbwald.DesktopTools.Contracts.Recovery;

public sealed record FileOperationRecoveryResult(
    FileOperationRecoveryCandidate Candidate,
    FileOperationRecoveryOutcome Outcome,
    string Message,
    bool RecoveryRetained);
