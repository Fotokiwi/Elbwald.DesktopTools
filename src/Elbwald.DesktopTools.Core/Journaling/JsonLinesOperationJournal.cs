using System.Text;
using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Journaling;

namespace Elbwald.DesktopTools.Core.Journaling;

public sealed class JsonLinesOperationJournal : IOperationJournal
{
    private readonly string _journalPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions =
        new(JsonSerializerDefaults.Web);

    public JsonLinesOperationJournal(
        string journalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        _journalPath = Path.GetFullPath(journalPath);
    }

    public async Task AppendAsync(
        OperationJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var directory =
                Path.GetDirectoryName(_journalPath)
                ?? throw new InvalidOperationException(
                    "Das Journal hat kein gültiges Verzeichnis.");

            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(
                entry,
                _jsonOptions);

            await using var stream = new FileStream(
                _journalPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);

            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                16 * 1024,
                leaveOpen: true);

            await writer.WriteLineAsync(
                json.AsMemory(),
                cancellationToken);

            await writer.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationJournalReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(_journalPath))
            {
                return new OperationJournalReadResult(
                    Array.Empty<OperationJournalEntry>(),
                    0);
            }

            var entries = new List<OperationJournalEntry>();
            var corruptLines = 0;

            await using var stream = new FileStream(
                _journalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                16 * 1024,
                leaveOpen: false);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(
                    cancellationToken);

                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry =
                        JsonSerializer.Deserialize<OperationJournalEntry>(
                            line,
                            _jsonOptions);

                    if (entry is null)
                    {
                        corruptLines++;
                        continue;
                    }

                    entries.Add(entry);
                }
                catch (JsonException)
                {
                    corruptLines++;
                }
            }

            return new OperationJournalReadResult(
                entries,
                corruptLines);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OperationJournalEntry>>
        GetIncompleteTransactionsAsync(
            CancellationToken cancellationToken = default)
    {
        var readResult = await ReadAsync(
            cancellationToken);

        return readResult.Entries
            .GroupBy(entry => entry.TransactionId)
            .Select(group => group.Last())
            .Where(entry => !IsTerminal(entry.State))
            .OrderBy(entry => entry.TimestampUtc)
            .ToArray();
    }

    private static bool IsTerminal(
        OperationJournalState state)
    {
        return state is
            OperationJournalState.Completed
            or OperationJournalState.Failed
            or OperationJournalState.Recovered
            or OperationJournalState.Cancelled;
    }
}
