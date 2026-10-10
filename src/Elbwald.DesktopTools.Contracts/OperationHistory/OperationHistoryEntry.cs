namespace Elbwald.DesktopTools.Contracts.OperationHistory;

public sealed record OperationHistoryEntry(
    string BatchId,
    string OperationType,
    string SourcePath,
    string DestinationPath,
    string Sha256,
    long Length,
    string? MediaItemId,
    DateTimeOffset CompletedUtc,
    string? ReversalOfBatchId = null);
