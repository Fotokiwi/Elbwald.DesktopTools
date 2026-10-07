using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Journaling;

public sealed class OperationJournalMaintenanceTests
{
    [Fact]
    public async Task AnalyzeIntegrityAsync_CleanJournal_IsClean()
    {
        using var directory = new TemporaryDirectory();

        var journal =
            CreateJournal(
                directory,
                out _);

        await journal.AppendAsync(
            CreateEntry(
                OperationJournalState.Completed));

        var report =
            await journal.AnalyzeIntegrityAsync();

        Assert.Equal(
            OperationJournalIntegrityState.Clean,
            report.State);

        Assert.Equal(
            0,
            report.CorruptLineCount);

        Assert.False(
            report.CanRepairAutomatically);
    }

    [Fact]
    public async Task AnalyzeIntegrityAsync_ValidLastLineWithoutNewline_IsClean()
    {
        using var directory = new TemporaryDirectory();

        var journal =
            CreateJournal(
                directory,
                out var journalPath);

        await journal.AppendAsync(
            CreateEntry(
                OperationJournalState.Completed));

        var bytes =
            await File.ReadAllBytesAsync(
                journalPath);

        Assert.Equal(
            (byte)'\n',
            bytes[^1]);

        await File.WriteAllBytesAsync(
            journalPath,
            bytes[..^1]);

        var report =
            await journal.AnalyzeIntegrityAsync();

        Assert.Equal(
            OperationJournalIntegrityState.Clean,
            report.State);
    }

    [Fact]
    public async Task AnalyzeIntegrityAsync_UnterminatedCorruptTail_IsRepairable()
    {
        using var directory = new TemporaryDirectory();

        var journal =
            CreateJournal(
                directory,
                out var journalPath);

        await journal.AppendAsync(
            CreateEntry(
                OperationJournalState.Completed));

        await File.AppendAllTextAsync(
            journalPath,
            "{\"transactionId\":");

        var report =
            await journal.AnalyzeIntegrityAsync();

        Assert.Equal(
            OperationJournalIntegrityState.RepairableTrailingRecord,
            report.State);

        Assert.Equal(
            1,
            report.CorruptLineCount);

        Assert.True(
            report.CanRepairAutomatically);

        Assert.NotNull(
            report.RepairablePrefixLengthBytes);
    }

    [Fact]
    public async Task AnalyzeIntegrityAsync_CorruptTerminatedLine_IsUnsafe()
    {
        using var directory = new TemporaryDirectory();

        var journal =
            CreateJournal(
                directory,
                out var journalPath);

        await journal.AppendAsync(
            CreateEntry(
                OperationJournalState.Completed));

        await File.AppendAllTextAsync(
            journalPath,
            "{invalid-json}\n");

        var report =
            await journal.AnalyzeIntegrityAsync();

        Assert.Equal(
            OperationJournalIntegrityState.UnsafeCorruption,
            report.State);

        Assert.False(
            report.CanRepairAutomatically);
    }

    [Fact]
    public async Task AnalyzeIntegrityAsync_CorruptionInMiddle_IsUnsafe()
    {
        using var directory = new TemporaryDirectory();

        var journal =
            CreateJournal(
                directory,
                out var journalPath);

        await journal.AppendAsync(
            CreateEntry(
                OperationJournalState.Prepared));

        await File.AppendAllTextAsync(
            journalPath,
            "{invalid-json}\n");

        await journal.AppendAsync(
            CreateEntry(
                OperationJournalState.Completed));

        var report =
            await journal.AnalyzeIntegrityAsync();

        Assert.Equal(
            OperationJournalIntegrityState.UnsafeCorruption,
            report.State);
    }

    [Fact]
    public async Task RepairTrailingRecordAsync_CreatesExactBackupAndPreservesValidEntries()
    {
        using var directory = new TemporaryDirectory();

        var journal =
            CreateJournal(
                directory,
                out var journalPath);

        var validEntry =
            CreateEntry(
                OperationJournalState.Completed);

        await journal.AppendAsync(
            validEntry);

        await File.AppendAllTextAsync(
            journalPath,
            "{\"transactionId\":");

        var originalBytes =
            await File.ReadAllBytesAsync(
                journalPath);

        var result =
            await journal.RepairTrailingRecordAsync();

        Assert.Equal(
            OperationJournalRepairOutcome.Repaired,
            result.Outcome);

        Assert.NotNull(
            result.BackupPath);

        Assert.True(
            File.Exists(result.BackupPath));

        Assert.Equal(
            originalBytes,
            await File.ReadAllBytesAsync(
                result.BackupPath!));

        var readResult =
            await journal.ReadAsync();

        Assert.False(
            readResult.HasCorruption);

        var restoredEntry =
            Assert.Single(
                readResult.Entries);

        Assert.Equal(
            validEntry.TransactionId,
            restoredEntry.TransactionId);

        var report =
            await journal.AnalyzeIntegrityAsync();

        Assert.Equal(
            OperationJournalIntegrityState.Clean,
            report.State);
    }

    [Fact]
    public async Task RepairTrailingRecordAsync_UnsafeCorruption_DoesNotModifyJournal()
    {
        using var directory = new TemporaryDirectory();

        var journal =
            CreateJournal(
                directory,
                out var journalPath);

        await journal.AppendAsync(
            CreateEntry(
                OperationJournalState.Completed));

        await File.AppendAllTextAsync(
            journalPath,
            "{invalid-json}\n");

        var originalBytes =
            await File.ReadAllBytesAsync(
                journalPath);

        var result =
            await journal.RepairTrailingRecordAsync();

        Assert.Equal(
            OperationJournalRepairOutcome.UnsafeCorruption,
            result.Outcome);

        Assert.Null(
            result.BackupPath);

        Assert.Equal(
            originalBytes,
            await File.ReadAllBytesAsync(
                journalPath));
    }

    private static JsonLinesOperationJournal CreateJournal(
        TemporaryDirectory directory,
        out string journalPath)
    {
        journalPath =
            directory.GetPath(
                "journal/operations.jsonl");

        return new JsonLinesOperationJournal(
            journalPath);
    }

    private static OperationJournalEntry CreateEntry(
        OperationJournalState state)
    {
        return OperationJournalEntry.Create(
            Guid.NewGuid(),
            state,
            "Copy",
            "/source/photo.jpg",
            "/target/photo.jpg");
    }
}
