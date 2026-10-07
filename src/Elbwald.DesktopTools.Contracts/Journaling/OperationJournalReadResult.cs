namespace Elbwald.DesktopTools.Contracts.Journaling;

public sealed class OperationJournalReadResult
{
    public OperationJournalReadResult(
        IEnumerable<OperationJournalEntry> entries,
        int skippedCorruptLineCount)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (skippedCorruptLineCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skippedCorruptLineCount));
        }

        Entries = entries.ToArray();
        SkippedCorruptLineCount = skippedCorruptLineCount;
    }

    public IReadOnlyList<OperationJournalEntry> Entries { get; }

    public int SkippedCorruptLineCount { get; }

    public bool HasCorruption => SkippedCorruptLineCount > 0;
}
