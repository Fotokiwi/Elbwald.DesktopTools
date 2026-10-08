using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.Recovery;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Recovery;

public sealed class FileOperationRecoveryCoordinatorTests
{
    [Fact]
    public async Task RecoverPendingAsync_MoveLostSource_RestoresSourceAndKeepsDestination()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg",
            "original-data");

        var destination = directory.GetPath(
            "sorted/source.jpg");

        var store = CreateStore(directory);
        var recovery = await store.PreserveAsync(source);

        var journal = CreateJournal(directory);
        var transactionId = Guid.NewGuid();

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                transactionId,
                OperationJournalState.Executing,
                "Move",
                source,
                destination,
                operationIndex: 0,
                recovery: recovery));

        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)!);

        File.Move(
            source,
            destination);

        var coordinator = CreateCoordinator(
            journal,
            store);

        var results =
            await coordinator.RecoverPendingAsync();

        var result = Assert.Single(results);

        Assert.Equal(
            FileOperationRecoveryOutcome.Recovered,
            result.Outcome);

        Assert.True(File.Exists(source));
        Assert.True(File.Exists(destination));

        Assert.Equal(
            "original-data",
            File.ReadAllText(source));

        Assert.Equal(
            "original-data",
            File.ReadAllText(destination));

        Assert.False(result.RecoveryRetained);
        Assert.False(File.Exists(recovery.StorageLocation));

        var journalResult = await journal.ReadAsync();

        Assert.Equal(
            OperationJournalState.Recovered,
            journalResult.Entries[^1].State);
    }

    [Fact]
    public async Task RecoverPendingAsync_MoveBothFilesExist_RequiresManualAction()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg",
            "source");

        var destination = directory.CreateFile(
            "destination.jpg",
            "destination");

        var store = CreateStore(directory);
        var recovery = await store.PreserveAsync(source);

        var journal = CreateJournal(directory);

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                Guid.NewGuid(),
                OperationJournalState.RecoveryRequired,
                "Move",
                source,
                destination,
                operationIndex: 0,
                recovery: recovery));

        var coordinator = CreateCoordinator(
            journal,
            store);

        var result = Assert.Single(
            await coordinator.RecoverPendingAsync());

        Assert.Equal(
            FileOperationRecoveryOutcome.ManualActionRequired,
            result.Outcome);

        Assert.True(result.RecoveryRetained);
        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("destination", File.ReadAllText(destination));
        Assert.True(File.Exists(recovery.StorageLocation));
    }

    [Fact]
    public async Task RecoverPendingAsync_CopyWithIdenticalTarget_FinalizesTransaction()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg",
            "same-data");

        var destination = directory.CreateFile(
            "destination.jpg",
            "same-data");

        var journal = CreateJournal(directory);

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                Guid.NewGuid(),
                OperationJournalState.Executing,
                "Copy",
                source,
                destination,
                operationIndex: 0));

        var store = CreateStore(directory);

        var coordinator = CreateCoordinator(
            journal,
            store);

        var result = Assert.Single(
            await coordinator.RecoverPendingAsync());

        Assert.Equal(
            FileOperationRecoveryOutcome.Finalized,
            result.Outcome);

        var journalResult = await journal.ReadAsync();

        Assert.Equal(
            OperationJournalState.Completed,
            journalResult.Entries[^1].State);
    }

    [Fact]
    public async Task RecoverPendingAsync_CopyWithDifferentTarget_RequiresManualAction()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg",
            "source-data");

        var destination = directory.CreateFile(
            "destination.jpg",
            "different-data");

        var journal = CreateJournal(directory);

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                Guid.NewGuid(),
                OperationJournalState.Committed,
                "Copy",
                source,
                destination,
                operationIndex: 0));

        var coordinator = CreateCoordinator(
            journal,
            CreateStore(directory));

        var result = Assert.Single(
            await coordinator.RecoverPendingAsync());

        Assert.Equal(
            FileOperationRecoveryOutcome.ManualActionRequired,
            result.Outcome);

        Assert.Equal(
            "source-data",
            File.ReadAllText(source));

        Assert.Equal(
            "different-data",
            File.ReadAllText(destination));
    }

    [Fact]
    public async Task RecoverPendingAsync_MoveWithoutRecoveryAndMissingSource_DoesNothing()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.GetPath(
            "missing-source.jpg");

        var destination = directory.CreateFile(
            "destination.jpg",
            "destination");

        var journal = CreateJournal(directory);

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                Guid.NewGuid(),
                OperationJournalState.Executing,
                "Move",
                source,
                destination,
                operationIndex: 0));

        var result = Assert.Single(
            await CreateCoordinator(
                    journal,
                    CreateStore(directory))
                .RecoverPendingAsync());

        Assert.Equal(
            FileOperationRecoveryOutcome.ManualActionRequired,
            result.Outcome);

        Assert.False(File.Exists(source));
        Assert.Equal(
            "destination",
            File.ReadAllText(destination));
    }

    private static JsonLinesOperationJournal CreateJournal(
        TemporaryDirectory directory)
    {
        return new JsonLinesOperationJournal(
            directory.GetPath(
                "journal/operations.jsonl"));
    }

    private static FileRecoveryStore CreateStore(
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

    private static FileOperationRecoveryCoordinator CreateCoordinator(
        IOperationJournal journal,
        IPersistentRecoveryStore store)
    {
        var inspector =
            new FileOperationRecoveryInspector(journal);

        return new FileOperationRecoveryCoordinator(
            inspector,
            journal,
            store,
            FixedFileOperationProcessLock.Held());
    }
}
