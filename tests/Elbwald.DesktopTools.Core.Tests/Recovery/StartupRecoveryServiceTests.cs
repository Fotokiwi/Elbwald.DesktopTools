using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.Recovery;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Recovery;

public sealed class StartupRecoveryServiceTests
{
    [Fact]
    public void Current_BeforeScan_IsNotScannedAndBlocksFileOperations()
    {
        using var directory = new TemporaryDirectory();

        var journal = CreateJournal(directory);
        var service = CreateService(journal);

        Assert.Equal(
            StartupRecoveryState.NotScanned,
            service.Current.State);

        Assert.False(
            service.Current.CanStartFileOperations);
    }

    [Fact]
    public async Task ScanAsync_EmptyJournal_IsClean()
    {
        using var directory = new TemporaryDirectory();

        var journal = CreateJournal(directory);
        var service = CreateService(journal);

        var result = await service.ScanAsync();

        Assert.Equal(
            StartupRecoveryState.Clean,
            result.State);

        Assert.True(result.CanStartFileOperations);
        Assert.False(result.RequiresAttention);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.CorruptJournalLineCount);
        Assert.NotNull(result.ScannedAtUtc);
    }

    [Fact]
    public async Task ScanAsync_IncompleteTransaction_RequiresAttention()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg",
            "source");

        var destination = directory.GetPath(
            "destination.jpg");

        var journal = CreateJournal(directory);

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                Guid.NewGuid(),
                OperationJournalState.Executing,
                "Move",
                source,
                destination,
                operationIndex: 0));

        var service = CreateService(journal);

        var result = await service.ScanAsync();

        Assert.Equal(
            StartupRecoveryState.AttentionRequired,
            result.State);

        Assert.True(result.RequiresAttention);
        Assert.False(result.CanStartFileOperations);

        var candidate = Assert.Single(result.Candidates);

        Assert.Equal("Move", candidate.OperationKind);
        Assert.True(candidate.SourceExists);
        Assert.False(candidate.DestinationExists);
    }

    [Fact]
    public async Task ScanAsync_CorruptJournalLine_RequiresAttention()
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

        var service = CreateService(journal);

        var result = await service.ScanAsync();

        Assert.Equal(
            StartupRecoveryState.AttentionRequired,
            result.State);

        Assert.True(result.HasJournalCorruption);
        Assert.Equal(1, result.CorruptJournalLineCount);
        Assert.False(result.CanStartFileOperations);
    }

    [Fact]
    public async Task ScanAsync_CompletedTransaction_IsClean()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg",
            "source");

        var destination = directory.GetPath(
            "destination.jpg");

        var journal = CreateJournal(directory);

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                Guid.NewGuid(),
                OperationJournalState.Completed,
                "Copy",
                source,
                destination,
                operationIndex: 0));

        var service = CreateService(journal);

        var result = await service.ScanAsync();

        Assert.Equal(
            StartupRecoveryState.Clean,
            result.State);

        Assert.True(result.CanStartFileOperations);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task ScanAsync_JournalReadFailure_IsScanFailed()
    {
        var journal = new ThrowingJournal();

        var service = new StartupRecoveryService(
            journal,
            new FileOperationRecoveryInspector(journal));

        var result = await service.ScanAsync();

        Assert.Equal(
            StartupRecoveryState.ScanFailed,
            result.State);

        Assert.True(result.RequiresAttention);
        Assert.False(result.CanStartFileOperations);
        Assert.Contains(
            "Simulierter Journalfehler",
            result.ErrorMessage);
    }

    private static JsonLinesOperationJournal CreateJournal(
        TemporaryDirectory directory)
    {
        return new JsonLinesOperationJournal(
            directory.GetPath(
                "journal/operations.jsonl"));
    }

    private static StartupRecoveryService CreateService(
        IOperationJournal journal)
    {
        return new StartupRecoveryService(
            journal,
            new FileOperationRecoveryInspector(journal));
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
