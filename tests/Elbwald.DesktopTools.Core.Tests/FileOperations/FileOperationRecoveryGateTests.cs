using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.Recovery;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.FileOperations;

public sealed class FileOperationRecoveryGateTests
{
    [Fact]
    public async Task ExecuteAsync_IncompleteTransaction_BlocksBeforeSafetyCheck()
    {
        using var directory = new TemporaryDirectory();

        var oldSource = directory.CreateFile(
            "old/source.jpg",
            "old");

        var oldDestination = directory.GetPath(
            "old/destination.jpg");

        var journal = CreateJournal(directory);

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                Guid.NewGuid(),
                OperationJournalState.Executing,
                "Move",
                oldSource,
                oldDestination,
                operationIndex: 0));

        var newSource = directory.CreateFile(
            "new/source.jpg",
            "new");

        var newDestination = directory.GetPath(
            "new/destination.jpg");

        var safetyChecker = new ThrowIfCalledSafetyChecker();

        var executor = CreateExecutor(
            directory,
            journal,
            safetyChecker);

        var plan = new FileOperationPlanner().CreatePlan([
            FileOperationRequest.Copy(
                newSource,
                newDestination)
        ]);

        var result = await executor.ExecuteAsync(plan);

        Assert.True(result.WasBlockedByRecoveryState);
        Assert.Equal(
            FileOperationExecutionState.BlockedByRecovery,
            result.State);

        Assert.Null(result.TransactionId);
        Assert.Empty(result.Items);

        Assert.Equal(0, safetyChecker.CallCount);

        Assert.True(File.Exists(newSource));
        Assert.False(File.Exists(newDestination));
        Assert.True(File.Exists(oldSource));
        Assert.False(File.Exists(oldDestination));
    }

    [Fact]
    public async Task ExecuteAsync_CorruptJournal_BlocksFileOperations()
    {
        using var directory = new TemporaryDirectory();

        var journalPath = directory.GetPath(
            "journal/operations.jsonl");

        Directory.CreateDirectory(
            Path.GetDirectoryName(journalPath)!);

        await File.WriteAllTextAsync(
            journalPath,
            "{invalid-json}\n");

        var journal =
            new JsonLinesOperationJournal(journalPath);

        var source = directory.CreateFile(
            "source.jpg",
            "source");

        var destination = directory.GetPath(
            "destination.jpg");

        var executor = CreateExecutor(
            directory,
            journal,
            new ThrowIfCalledSafetyChecker());

        var plan = new FileOperationPlanner().CreatePlan([
            FileOperationRequest.Move(
                source,
                destination)
        ]);

        var result = await executor.ExecuteAsync(plan);

        Assert.True(result.WasBlockedByRecoveryState);
        Assert.Contains(
            "beschädigte Journalzeilen",
            result.ErrorMessage);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ExecuteAsync_RecoveryScanFails_BlocksFileOperations()
    {
        using var directory = new TemporaryDirectory();

        var journal = new ThrowingJournal();

        var recoveryService =
            new StartupRecoveryService(
                journal,
                new FileOperationRecoveryInspector(journal));

        var source = directory.CreateFile(
            "source.jpg",
            "source");

        var destination = directory.GetPath(
            "destination.jpg");

        var recoveryStore = CreateRecoveryStore(directory);

        var executor = new FileOperationExecutor(
            new ThrowIfCalledSafetyChecker(),
            journal,
            recoveryStore,
            recoveryService);

        var plan = new FileOperationPlanner().CreatePlan([
            FileOperationRequest.Copy(
                source,
                destination)
        ]);

        var result = await executor.ExecuteAsync(plan);

        Assert.True(result.WasBlockedByRecoveryState);
        Assert.Contains(
            "Recovery-Prüfung",
            result.ErrorMessage);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
    }

    private static JsonLinesOperationJournal CreateJournal(
        TemporaryDirectory directory)
    {
        return new JsonLinesOperationJournal(
            directory.GetPath(
                "journal/operations.jsonl"));
    }

    private static FileOperationExecutor CreateExecutor(
        TemporaryDirectory directory,
        IOperationJournal journal,
        IFileOperationSafetyChecker safetyChecker)
    {
        var recoveryStore =
            CreateRecoveryStore(directory);

        var recoveryService =
            new StartupRecoveryService(
                journal,
                new FileOperationRecoveryInspector(journal));

        return new FileOperationExecutor(
            safetyChecker,
            journal,
            recoveryStore,
            recoveryService);
    }

    private static FileRecoveryStore CreateRecoveryStore(
        TemporaryDirectory directory)
    {
        return new FileRecoveryStore(
            new Elbwald.DesktopTools.Contracts.Recovery.RecoveryOptions
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

    private sealed class ThrowIfCalledSafetyChecker
        : IFileOperationSafetyChecker
    {
        public int CallCount { get; private set; }

        public FileOperationPreflightResult Check(
            FileOperationPlan plan)
        {
            CallCount++;

            throw new InvalidOperationException(
                "Safety checker must not run while recovery is blocked.");
        }

        public FileOperationPreflightResult CheckOperation(
            FileOperationPlanItem operation)
        {
            CallCount++;

            throw new InvalidOperationException(
                "Safety checker must not run while recovery is blocked.");
        }
    }

    private sealed class ThrowingJournal
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
            throw new IOException(
                "Simulierter Journalfehler.");
        }

        public Task<IReadOnlyList<OperationJournalEntry>>
            GetIncompleteTransactionsAsync(
                CancellationToken cancellationToken = default)
        {
            throw new IOException(
                "Simulierter Journalfehler.");
        }
    }
}
