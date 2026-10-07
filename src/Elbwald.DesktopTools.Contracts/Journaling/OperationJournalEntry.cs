using Elbwald.DesktopTools.Contracts.Recovery;

namespace Elbwald.DesktopTools.Contracts.Journaling;

public sealed record OperationJournalEntry(
    Guid TransactionId,
    Guid EntryId,
    DateTimeOffset TimestampUtc,
    OperationJournalState State,
    string OperationKind,
    string SourcePath,
    string DestinationPath,
    int? OperationIndex = null,
    RecoveryHandle? Recovery = null,
    string? Message = null)
{
    public string? RecoveryHandleId => Recovery?.Id;

    public static OperationJournalEntry Create(
        Guid transactionId,
        OperationJournalState state,
        string operationKind,
        string sourcePath,
        string destinationPath,
        int? operationIndex = null,
        RecoveryHandle? recovery = null,
        string? message = null)
    {
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Die TransactionId darf nicht leer sein.",
                nameof(transactionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        return new OperationJournalEntry(
            transactionId,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            state,
            operationKind,
            sourcePath,
            destinationPath,
            operationIndex,
            recovery,
            message);
    }
}
