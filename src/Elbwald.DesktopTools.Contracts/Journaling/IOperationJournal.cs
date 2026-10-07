namespace Elbwald.DesktopTools.Contracts.Journaling;

public interface IOperationJournal
{
    Task AppendAsync(
        OperationJournalEntry entry,
        CancellationToken cancellationToken = default);

    Task<OperationJournalReadResult> ReadAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationJournalEntry>> GetIncompleteTransactionsAsync(
        CancellationToken cancellationToken = default);
}
