using Elbwald.DesktopTools.Contracts.Journaling;

namespace Elbwald.DesktopTools.Contracts.Recovery;

public sealed record FileOperationRecoveryCandidate(
    Guid TransactionId,
    OperationJournalState LastKnownState,
    string OperationKind,
    string SourcePath,
    string DestinationPath,
    int? OperationIndex,
    RecoveryHandle? Recovery,
    bool SourceExists,
    bool DestinationExists);
