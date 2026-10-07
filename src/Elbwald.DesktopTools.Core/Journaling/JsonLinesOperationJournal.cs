using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Journaling;

namespace Elbwald.DesktopTools.Core.Journaling;

public sealed class JsonLinesOperationJournal
    : IOperationJournal,
      IOperationJournalMaintenance
{
    private const int IoBufferSize = 64 * 1024;
    private const int MaxJournalLineBytes = 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

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
            return await ReadCoreAsync(
                cancellationToken);
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

    public async Task<OperationJournalIntegrityReport>
        AnalyzeIntegrityAsync(
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            return await AnalyzeIntegrityCoreAsync(
                _journalPath,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationJournalRepairResult>
        RepairTrailingRecordAsync(
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        string? backupPath = null;
        string? backupTemporaryPath = null;
        string? repairedTemporaryPath = null;

        try
        {
            var analysis =
                await AnalyzeIntegrityCoreAsync(
                    _journalPath,
                    cancellationToken);

            if (analysis.State == OperationJournalIntegrityState.Clean)
            {
                return new OperationJournalRepairResult(
                    OperationJournalRepairOutcome.NotNeeded,
                    "Das Operationsjournal ist bereits sauber.");
            }

            if (!analysis.CanRepairAutomatically
                || analysis.RepairablePrefixLengthBytes is null)
            {
                return new OperationJournalRepairResult(
                    OperationJournalRepairOutcome.UnsafeCorruption,
                    "Die Journal-Korruption ist nicht auf einen eindeutig "
                    + "unvollständigen letzten Datensatz begrenzt. "
                    + "Es wurde nichts verändert.");
            }

            var directory =
                Path.GetDirectoryName(_journalPath)
                ?? throw new InvalidOperationException(
                    "Das Journal hat kein gültiges Verzeichnis.");

            Directory.CreateDirectory(directory);

            var suffix =
                DateTimeOffset.UtcNow.ToString(
                    "yyyyMMddTHHmmssfff'Z'",
                    CultureInfo.InvariantCulture)
                + "-"
                + Guid.NewGuid().ToString("N");

            backupPath =
                _journalPath
                + ".corrupt-"
                + suffix
                + ".backup";

            backupTemporaryPath =
                backupPath
                + ".partial";

            repairedTemporaryPath =
                _journalPath
                + ".repair-"
                + Guid.NewGuid().ToString("N")
                + ".partial";

            var backupSnapshot =
                await CopyFileWithHashAsync(
                    _journalPath,
                    backupTemporaryPath,
                    cancellationToken);

            File.Move(
                backupTemporaryPath,
                backupPath);

            backupTemporaryPath = null;

            if (backupSnapshot.Length
                != analysis.JournalLengthBytes)
            {
                return new OperationJournalRepairResult(
                    OperationJournalRepairOutcome.Failed,
                    "Das Journal hat sich während der Sicherung verändert. "
                    + "Die Reparatur wurde nicht committed.",
                    backupPath);
            }

            await CopyPrefixAsync(
                backupPath,
                repairedTemporaryPath,
                analysis.RepairablePrefixLengthBytes.Value,
                cancellationToken);

            var repairedAnalysis =
                await AnalyzeIntegrityCoreAsync(
                    repairedTemporaryPath,
                    cancellationToken);

            if (!repairedAnalysis.IsClean)
            {
                return new OperationJournalRepairResult(
                    OperationJournalRepairOutcome.Failed,
                    "Die vorbereitete Reparatur hat die Integritätsprüfung "
                    + "nicht bestanden. Das Originaljournal bleibt bestehen.",
                    backupPath);
            }

            var currentSnapshot =
                await ComputeFileHashAsync(
                    _journalPath,
                    cancellationToken);

            if (currentSnapshot.Length != backupSnapshot.Length
                || !CryptographicOperations.FixedTimeEquals(
                    currentSnapshot.Hash,
                    backupSnapshot.Hash))
            {
                return new OperationJournalRepairResult(
                    OperationJournalRepairOutcome.Failed,
                    "Das Journal wurde während der Reparatur verändert. "
                    + "Aus Sicherheitsgründen wurde der Commit abgebrochen.",
                    backupPath);
            }

            File.Move(
                repairedTemporaryPath,
                _journalPath,
                overwrite: true);

            repairedTemporaryPath = null;

            var committedAnalysis =
                await AnalyzeIntegrityCoreAsync(
                    _journalPath,
                    cancellationToken);

            if (!committedAnalysis.IsClean)
            {
                return new OperationJournalRepairResult(
                    OperationJournalRepairOutcome.Failed,
                    "Das reparierte Journal ist nach dem Commit nicht sauber. "
                    + "Die vollständige Originalkopie wurde aufbewahrt.",
                    backupPath);
            }

            return new OperationJournalRepairResult(
                OperationJournalRepairOutcome.Repaired,
                "Der eindeutig unvollständige letzte Journal-Datensatz wurde "
                + "entfernt. Das vollständige beschädigte Originaljournal "
                + "wurde als Backup aufbewahrt.",
                backupPath);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException
                or NotSupportedException
                or DecoderFallbackException
                or JsonException)
        {
            return new OperationJournalRepairResult(
                OperationJournalRepairOutcome.Failed,
                "Die Journal-Reparatur konnte nicht sicher abgeschlossen "
                + $"werden: {exception.Message}",
                backupPath);
        }
        finally
        {
            TryDeleteTemporaryFile(
                backupTemporaryPath);

            TryDeleteTemporaryFile(
                repairedTemporaryPath);

            _gate.Release();
        }
    }

    private async Task<OperationJournalReadResult> ReadCoreAsync(
        CancellationToken cancellationToken)
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

    private async Task<OperationJournalIntegrityReport>
        AnalyzeIntegrityCoreAsync(
            string path,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new OperationJournalIntegrityReport(
                OperationJournalIntegrityState.Clean,
                CorruptLineCount: 0,
                JournalLengthBytes: 0);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            IoBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var journalLength = stream.Length;
        var buffer = new byte[IoBufferSize];

        using var lineBuffer = new MemoryStream();

        long absoluteOffset = 0;
        long lineStartOffset = 0;
        var corruptLineCount = 0;
        var hasUnsafeCorruption = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            for (var index = 0; index < bytesRead; index++)
            {
                var value = buffer[index];

                if (value == (byte)'\n')
                {
                    if (!IsValidJournalLine(
                            lineBuffer.ToArray()))
                    {
                        corruptLineCount++;
                        hasUnsafeCorruption = true;
                    }

                    lineBuffer.SetLength(0);
                    lineStartOffset =
                        absoluteOffset + 1;
                }
                else
                {
                    if (lineBuffer.Length
                        >= MaxJournalLineBytes)
                    {
                        return new OperationJournalIntegrityReport(
                            OperationJournalIntegrityState.UnsafeCorruption,
                            CorruptLineCount:
                                Math.Max(
                                    1,
                                    corruptLineCount),
                            JournalLengthBytes:
                                journalLength);
                    }

                    lineBuffer.WriteByte(value);
                }

                absoluteOffset++;
            }
        }

        if (lineBuffer.Length > 0)
        {
            var finalLineIsValid =
                IsValidJournalLine(
                    lineBuffer.ToArray());

            if (!finalLineIsValid)
            {
                corruptLineCount++;

                if (!hasUnsafeCorruption
                    && corruptLineCount == 1)
                {
                    return new OperationJournalIntegrityReport(
                        OperationJournalIntegrityState.RepairableTrailingRecord,
                        CorruptLineCount: 1,
                        JournalLengthBytes: journalLength,
                        RepairablePrefixLengthBytes:
                            lineStartOffset);
                }

                hasUnsafeCorruption = true;
            }
        }

        if (hasUnsafeCorruption)
        {
            return new OperationJournalIntegrityReport(
                OperationJournalIntegrityState.UnsafeCorruption,
                corruptLineCount,
                journalLength);
        }

        return new OperationJournalIntegrityReport(
            OperationJournalIntegrityState.Clean,
            CorruptLineCount: 0,
            JournalLengthBytes: journalLength);
    }

    private bool IsValidJournalLine(
        byte[] lineBytes)
    {
        var length = lineBytes.Length;

        if (length > 0
            && lineBytes[length - 1] == (byte)'\r')
        {
            length--;
        }

        if (length == 0)
        {
            return true;
        }

        string line;

        try
        {
            line = StrictUtf8.GetString(
                lineBytes,
                0,
                length);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        try
        {
            return JsonSerializer.Deserialize<OperationJournalEntry>(
                line,
                _jsonOptions) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<FileHashSnapshot>
        CopyFileWithHashAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            IoBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            IoBufferSize,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);

        var buffer = new byte[IoBufferSize];
        long length = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            hash.AppendData(
                buffer,
                0,
                bytesRead);

            await destination.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);

            length += bytesRead;
        }

        await destination.FlushAsync(
            cancellationToken);

        destination.Flush(
            flushToDisk: true);

        return new FileHashSnapshot(
            length,
            hash.GetHashAndReset());
    }

    private static async Task CopyPrefixAsync(
        string sourcePath,
        string destinationPath,
        long bytesToCopy,
        CancellationToken cancellationToken)
    {
        if (bytesToCopy < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytesToCopy));
        }

        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            IoBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            IoBufferSize,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        var buffer = new byte[IoBufferSize];
        var remaining = bytesToCopy;

        while (remaining > 0)
        {
            var requested =
                (int)Math.Min(
                    buffer.Length,
                    remaining);

            var bytesRead = await source.ReadAsync(
                buffer.AsMemory(
                    0,
                    requested),
                cancellationToken);

            if (bytesRead == 0)
            {
                throw new EndOfStreamException(
                    "Das Journal endete während der Reparatur unerwartet.");
            }

            await destination.WriteAsync(
                buffer.AsMemory(
                    0,
                    bytesRead),
                cancellationToken);

            remaining -= bytesRead;
        }

        await destination.FlushAsync(
            cancellationToken);

        destination.Flush(
            flushToDisk: true);
    }

    private static async Task<FileHashSnapshot>
        ComputeFileHashAsync(
            string path,
            CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            IoBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);

        var buffer = new byte[IoBufferSize];
        long length = 0;

        while (true)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            hash.AppendData(
                buffer,
                0,
                bytesRead);

            length += bytesRead;
        }

        return new FileHashSnapshot(
            length,
            hash.GetHashAndReset());
    }

    private static void TryDeleteTemporaryFile(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A stale .partial is safer than hiding the original repair error.
        }
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

    private sealed record FileHashSnapshot(
        long Length,
        byte[] Hash);
}
