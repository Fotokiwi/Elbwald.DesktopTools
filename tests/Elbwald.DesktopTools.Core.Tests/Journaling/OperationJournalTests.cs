using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Journaling;

public sealed class OperationJournalTests
{
    [Fact]
    public async Task AppendAndRead_RoundTripsEntries()
    {
        using var directory = new TemporaryDirectory();

        var journalPath = directory.GetPath(
            "journal/operations.jsonl");

        var journal = new JsonLinesOperationJournal(
            journalPath);

        var transactionId = Guid.NewGuid();

        var prepared = OperationJournalEntry.Create(
            transactionId,
            OperationJournalState.Prepared,
            "Move",
            "/source/a.jpg",
            "/target/a.jpg");

        var executing = OperationJournalEntry.Create(
            transactionId,
            OperationJournalState.Executing,
            "Move",
            "/source/a.jpg",
            "/target/a.jpg");

        await journal.AppendAsync(prepared);
        await journal.AppendAsync(executing);

        var result = await journal.ReadAsync();

        Assert.False(result.HasCorruption);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(
            OperationJournalState.Prepared,
            result.Entries[0].State);
        Assert.Equal(
            OperationJournalState.Executing,
            result.Entries[1].State);
    }

    [Fact]
    public async Task GetIncompleteTransactions_ReturnsOnlyNonTerminalTransactions()
    {
        using var directory = new TemporaryDirectory();

        var journal = new JsonLinesOperationJournal(
            directory.GetPath(
                "journal/operations.jsonl"));

        var incompleteId = Guid.NewGuid();
        var completedId = Guid.NewGuid();

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                incompleteId,
                OperationJournalState.Prepared,
                "Copy",
                "/source/incomplete.jpg",
                "/target/incomplete.jpg"));

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                incompleteId,
                OperationJournalState.Executing,
                "Copy",
                "/source/incomplete.jpg",
                "/target/incomplete.jpg"));

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                completedId,
                OperationJournalState.Prepared,
                "Copy",
                "/source/completed.jpg",
                "/target/completed.jpg"));

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                completedId,
                OperationJournalState.Completed,
                "Copy",
                "/source/completed.jpg",
                "/target/completed.jpg"));

        var incomplete =
            await journal.GetIncompleteTransactionsAsync();

        var entry = Assert.Single(incomplete);

        Assert.Equal(
            incompleteId,
            entry.TransactionId);

        Assert.Equal(
            OperationJournalState.Executing,
            entry.State);
    }

    [Fact]
    public async Task Read_CorruptLine_IsReportedAndValidEntriesSurvive()
    {
        using var directory = new TemporaryDirectory();

        var journalPath = directory.GetPath(
            "journal/operations.jsonl");

        var journal = new JsonLinesOperationJournal(
            journalPath);

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                Guid.NewGuid(),
                OperationJournalState.Prepared,
                "Copy",
                "/source/a.jpg",
                "/target/a.jpg"));

        await File.AppendAllTextAsync(
            journalPath,
            "{this is not valid json}\n");

        var result = await journal.ReadAsync();

        Assert.True(result.HasCorruption);
        Assert.Equal(1, result.SkippedCorruptLineCount);
        Assert.Single(result.Entries);
    }
}
