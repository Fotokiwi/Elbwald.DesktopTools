namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public sealed record MediaImportExecutionResult(
    MediaImportExecutionState State,
    int PlannedCopyCount,
    int CopiedCount,
    int AlreadyImportedCount,
    int ConflictCount,
    int RemainingCount,
    string Message,
    Guid? TransactionId = null);
