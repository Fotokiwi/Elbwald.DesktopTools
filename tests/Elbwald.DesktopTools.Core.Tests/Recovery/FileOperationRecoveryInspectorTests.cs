using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.Recovery;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Recovery;

public sealed class FileOperationRecoveryInspectorTests
{
    [Fact]
    public async Task InspectAsync_ReturnsIncompleteTransactionWithFilesystemState()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg",
            "source");

        var destination = directory.GetPath(
            "destination.jpg");

        var journal = new JsonLinesOperationJournal(
            directory.GetPath(
                "journal/operations.jsonl"));

        var transactionId = Guid.NewGuid();

        var recovery = new RecoveryHandle(
            "recovery-1",
            source,
            6,
            "ABCDEF",
            File.GetLastWriteTimeUtc(source),
            DateTimeOffset.UtcNow,
            RecoveryStorageKind.PersistentFile,
            directory.GetPath("recovery/recovery-1.recovery"));

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                transactionId,
                OperationJournalState.Executing,
                "Move",
                source,
                destination,
                operationIndex: 0,
                recovery: recovery));

        File.Move(
            source,
            destination);

        var inspector =
            new FileOperationRecoveryInspector(journal);

        var candidates =
            await inspector.InspectAsync();

        var candidate = Assert.Single(candidates);

        Assert.Equal(
            transactionId,
            candidate.TransactionId);

        Assert.Equal(
            OperationJournalState.Executing,
            candidate.LastKnownState);

        Assert.False(candidate.SourceExists);
        Assert.True(candidate.DestinationExists);
        Assert.Equal(
            recovery.Id,
            candidate.Recovery?.Id);
    }

    [Fact]
    public async Task InspectAsync_CompletedTransaction_IsNotReturned()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg");

        var destination = directory.GetPath(
            "destination.jpg");

        var journal = new JsonLinesOperationJournal(
            directory.GetPath(
                "journal/operations.jsonl"));

        var transactionId = Guid.NewGuid();

        await journal.AppendAsync(
            OperationJournalEntry.Create(
                transactionId,
                OperationJournalState.Completed,
                "Copy",
                source,
                destination));

        var inspector =
            new FileOperationRecoveryInspector(journal);

        Assert.Empty(
            await inspector.InspectAsync());
    }
}
