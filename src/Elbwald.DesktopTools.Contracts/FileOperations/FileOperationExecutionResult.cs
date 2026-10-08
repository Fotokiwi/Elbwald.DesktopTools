namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed class FileOperationExecutionResult
{
    public FileOperationExecutionResult(
        FileOperationExecutionState state,
        IEnumerable<FileOperationItemResult> items,
        int totalOperationCount,
        IEnumerable<FileOperationSafetyIssue>? safetyIssues = null,
        Guid? transactionId = null,
        string? errorMessage = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (totalOperationCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalOperationCount),
                totalOperationCount,
                "Die Gesamtzahl der Operationen darf nicht negativ sein.");
        }

        State = state;
        Items = items.ToArray();
        TotalOperationCount = totalOperationCount;
        SafetyIssues = safetyIssues?.ToArray()
            ?? Array.Empty<FileOperationSafetyIssue>();
        TransactionId = transactionId;
        ErrorMessage = errorMessage;
    }

    public FileOperationExecutionState State { get; }

    public IReadOnlyList<FileOperationItemResult> Items { get; }

    public IReadOnlyList<FileOperationSafetyIssue> SafetyIssues { get; }

    public int TotalOperationCount { get; }

    public Guid? TransactionId { get; }

    public string? ErrorMessage { get; }

    public int CompletedCount =>
        Items.Count(item => item.Status == FileOperationItemStatus.Completed);

    public int FailedCount =>
        Items.Count(item => item.Status == FileOperationItemStatus.Failed);

    public int RemainingCount =>
        Math.Max(
            0,
            TotalOperationCount - Items.Count);

    public bool IsSuccessful =>
        State == FileOperationExecutionState.Completed;

    public bool WasCancelled =>
        State == FileOperationExecutionState.Cancelled;

    public bool WasBlockedBySafetyCheck =>
        State == FileOperationExecutionState.Failed
        && SafetyIssues.Count > 0;

    public bool WasBlockedByRecoveryState =>
        State == FileOperationExecutionState.BlockedByRecovery;

    public bool WasBlockedByProcessLock =>
        State == FileOperationExecutionState.BlockedByProcessLock;

    public bool RequiresRecovery =>
        State == FileOperationExecutionState.RecoveryRequired;
}
