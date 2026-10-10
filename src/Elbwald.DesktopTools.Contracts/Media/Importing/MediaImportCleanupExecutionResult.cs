namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public sealed record MediaImportCleanupExecutionResult(
    MediaImportCleanupExecutionState State,
    int PlannedFileCount,
    int DeletedFileCount,
    int RemovedDirectoryCount,
    string Message,
    Guid CorrelationId);
