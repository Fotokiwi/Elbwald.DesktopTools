namespace Elbwald.DesktopTools.Contracts.OperationHistory;

public interface IOperationHistoryStore
{
    Task AppendBatchAsync(IEnumerable<OperationHistoryEntry> entries, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationHistoryEntry>> GetRecentAsync(int maxCount = 200, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationHistoryEntry>> GetBatchAsync(string batchId, CancellationToken cancellationToken = default);
    Task<bool> HasReversalAsync(string batchId, CancellationToken cancellationToken = default);
}
