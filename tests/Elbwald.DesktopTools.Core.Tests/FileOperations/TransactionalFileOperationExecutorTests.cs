using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.Recovery;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.FileOperations;

public sealed class TransactionalFileOperationExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Move_WritesCompleteTransactionAndReleasesRecovery()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "incoming/photo.jpg",
            "photo-data");

        var destination = directory.GetPath(
            "sorted/photo.jpg");

        var journal = new JsonLinesOperationJournal(
            directory.GetPath(
                "journal/operations.jsonl"));

        var persistentStore = CreatePersistentStore(directory);

        var executor = new FileOperationExecutor(
            new AlwaysSafeSafetyChecker(),
            journal,
            persistentStore);

        var plan = new FileOperationPlanner().CreatePlan([
            FileOperationRequest.Move(
                source,
                destination)
        ]);

        var result = await executor.ExecuteAsync(plan);

        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.TransactionId);

        var journalResult = await journal.ReadAsync();

        var transactionEntries = journalResult.Entries
            .Where(entry =>
                entry.TransactionId == result.TransactionId)
            .ToArray();

        Assert.Contains(
            transactionEntries,
            entry => entry.State == OperationJournalState.Prepared
                && entry.Recovery is not null);

        Assert.Contains(
            transactionEntries,
            entry => entry.State == OperationJournalState.Executing);

        Assert.Contains(
            transactionEntries,
            entry => entry.State == OperationJournalState.Committed);

        Assert.Equal(
            OperationJournalState.Completed,
            transactionEntries[^1].State);

        var recoveryDirectory =
            directory.GetPath("recovery");

        Assert.Empty(
            Directory.Exists(recoveryDirectory)
                ? Directory.EnumerateFiles(
                    recoveryDirectory,
                    "*.recovery",
                    SearchOption.TopDirectoryOnly)
                : Array.Empty<string>());
    }

    [Fact]
    public async Task ExecuteAsync_JournalFailureBeforeMove_LeavesSourceUntouched()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg",
            "source");

        var destination = directory.GetPath(
            "destination.jpg");

        var executor = new FileOperationExecutor(
            new AlwaysSafeSafetyChecker(),
            new AlwaysFailingJournal(),
            CreatePersistentStore(directory));

        var plan = new FileOperationPlanner().CreatePlan([
            FileOperationRequest.Move(
                source,
                destination)
        ]);

        var result = await executor.ExecuteAsync(plan);

        Assert.Equal(
            FileOperationExecutionState.Failed,
            result.State);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ExecuteAsync_FinalJournalFailureAfterMove_RequiresRecovery()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg",
            "source");

        var destination = directory.GetPath(
            "destination.jpg");

        var journal = new FailOnCompletedJournal();

        var persistentStore =
            CreatePersistentStore(directory);

        var executor = new FileOperationExecutor(
            new AlwaysSafeSafetyChecker(),
            journal,
            persistentStore);

        var plan = new FileOperationPlanner().CreatePlan([
            FileOperationRequest.Move(
                source,
                destination)
        ]);

        var result = await executor.ExecuteAsync(plan);

        Assert.True(result.RequiresRecovery);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(destination));

        var recoveryFiles = Directory.EnumerateFiles(
                directory.GetPath("recovery"),
                "*.recovery",
                SearchOption.TopDirectoryOnly)
            .ToArray();

        Assert.Single(recoveryFiles);

        Assert.Contains(
            journal.Entries,
            entry => entry.State == OperationJournalState.Committed
                && entry.Recovery is not null);
    }

    private static FileRecoveryStore CreatePersistentStore(
        TemporaryDirectory directory)
    {
        return new FileRecoveryStore(
            new RecoveryOptions
            {
                PersistentDirectory =
                    directory.GetPath("recovery"),
                MaxMemoryBytes = 0,
                MaxSingleMemoryItemBytes = 0,
                MinimumPersistentFreeSpaceReserveBytes = 0,
                PersistentFreeSpaceReserveRatio = 0,
                MaximumPersistentRatioReserveBytes = 0
            });
    }

    private sealed class AlwaysSafeSafetyChecker
        : IFileOperationSafetyChecker
    {
        public FileOperationPreflightResult Check(
            FileOperationPlan plan)
        {
            return FileOperationPreflightResult.Safe;
        }

        public FileOperationPreflightResult CheckOperation(
            FileOperationPlanItem operation)
        {
            return FileOperationPreflightResult.Safe;
        }
    }

    private sealed class AlwaysFailingJournal
        : IOperationJournal
    {
        public Task AppendAsync(
            OperationJournalEntry entry,
            CancellationToken cancellationToken = default)
        {
            throw new IOException(
                "Simulierter Journalfehler.");
        }

        public Task<OperationJournalReadResult> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new OperationJournalReadResult(
                    Array.Empty<OperationJournalEntry>(),
                    0));
        }

        public Task<IReadOnlyList<OperationJournalEntry>>
            GetIncompleteTransactionsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OperationJournalEntry>>(
                Array.Empty<OperationJournalEntry>());
        }
    }

    private sealed class FailOnCompletedJournal
        : IOperationJournal
    {
        public List<OperationJournalEntry> Entries { get; } = [];

        public Task AppendAsync(
            OperationJournalEntry entry,
            CancellationToken cancellationToken = default)
        {
            if (entry.State == OperationJournalState.Completed)
            {
                throw new IOException(
                    "Simulierter Fehler beim finalen Journal-Commit.");
            }

            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<OperationJournalReadResult> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new OperationJournalReadResult(
                    Entries,
                    0));
        }

        public Task<IReadOnlyList<OperationJournalEntry>>
            GetIncompleteTransactionsAsync(
                CancellationToken cancellationToken = default)
        {
            var latest = Entries
                .GroupBy(entry => entry.TransactionId)
                .Select(group => group.Last())
                .Where(entry => entry.State != OperationJournalState.Completed)
                .ToArray();

            return Task.FromResult<IReadOnlyList<OperationJournalEntry>>(
                latest);
        }
    }
}
